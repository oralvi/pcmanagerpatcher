#include <windows.h>
#include <detours.h>
#include <oleauto.h>

typedef HRESULT (*FnCWbemGet)(void*, LPCWSTR, long, VARIANT*, long*, long*);

static const WCHAR g_product[] = L"TM2424";
static const WCHAR g_manufacturer[] = L"XIAOMI";
static const WCHAR g_classProperty[] = L"__CLASS";
static const WCHAR g_baseBoardClass[] = L"Win32_BaseBoard";
static const CHAR g_fastproxGetSymbol[] = "?Get@CWbemObject@@UEAAJPEBGJPEAUtagVARIANT@@PEAJ2@Z";

static HMODULE g_fastproxModule = NULL;
static FnCWbemGet Real_CWbemGet = NULL;
static LONG g_hookInstalled = 0;

static void DebugOutA(const char* message)
{
    OutputDebugStringA(message);
}

static BOOL IsBaseBoardObject(void* pThis, long lFlags)
{
    VARIANT classValue;
    HRESULT hr;
    BOOL isBaseBoard = FALSE;

    if (Real_CWbemGet == NULL)
    {
        return FALSE;
    }

    VariantInit(&classValue);
    hr = Real_CWbemGet(pThis, g_classProperty, lFlags, &classValue, NULL, NULL);
    if (SUCCEEDED(hr) &&
        classValue.vt == VT_BSTR &&
        classValue.bstrVal != NULL &&
        lstrcmpW(classValue.bstrVal, g_baseBoardClass) == 0)
    {
        isBaseBoard = TRUE;
    }

    VariantClear(&classValue);
    return isBaseBoard;
}

static void ReplaceBstrValue(VARIANT* pVal, const WCHAR* replacement)
{
    BSTR newValue;

    if (pVal == NULL || pVal->vt != VT_BSTR)
    {
        return;
    }

    newValue = SysAllocString(replacement);
    if (newValue == NULL)
    {
        DebugOutA("[hook.dll] SysAllocString failed");
        return;
    }

    SysFreeString(pVal->bstrVal);
    pVal->bstrVal = newValue;
}

static HRESULT Hook_CWbemGet(
    void* pThis,
    LPCWSTR wszName,
    long lFlags,
    VARIANT* pVal,
    long* pType,
    long* plFlavor)
{
    HRESULT hr;

    if (Real_CWbemGet == NULL)
    {
        return E_FAIL;
    }

    hr = Real_CWbemGet(pThis, wszName, lFlags, pVal, pType, plFlavor);
    if (!SUCCEEDED(hr) || pVal == NULL || wszName == NULL)
    {
        return hr;
    }

    if (!IsBaseBoardObject(pThis, lFlags))
    {
        return hr;
    }

    if (lstrcmpW(wszName, L"Product") == 0)
    {
        ReplaceBstrValue(pVal, g_product);
    }
    else if (lstrcmpW(wszName, L"Manufacturer") == 0)
    {
        ReplaceBstrValue(pVal, g_manufacturer);
    }

    return hr;
}

static DWORD WINAPI InstallHookThread(LPVOID parameter)
{
    WCHAR path[MAX_PATH];
    LONG previousState;
    LONG detourError;

    (void)parameter;

    previousState = InterlockedCompareExchange(&g_hookInstalled, 0, 0);
    if (previousState != 0)
    {
        return 0;
    }

    if (GetSystemDirectoryW(path, MAX_PATH) == 0)
    {
        DebugOutA("[hook.dll] GetSystemDirectoryW failed");
        return 1;
    }

    if (lstrlenW(path) + lstrlenW(L"\\wbem\\fastprox.dll") >= MAX_PATH)
    {
        DebugOutA("[hook.dll] fastprox path too long");
        return 1;
    }

    lstrcatW(path, L"\\wbem\\fastprox.dll");
    g_fastproxModule = LoadLibraryW(path);
    if (g_fastproxModule == NULL)
    {
        DebugOutA("[hook.dll] LoadLibraryW(fastprox.dll) failed");
        return 1;
    }

    Real_CWbemGet = (FnCWbemGet)GetProcAddress(g_fastproxModule, g_fastproxGetSymbol);
    if (Real_CWbemGet == NULL)
    {
        DebugOutA("[hook.dll] GetProcAddress(CWbemObject::Get) failed");
        return 1;
    }

    detourError = DetourTransactionBegin();
    if (detourError != NO_ERROR)
    {
        DebugOutA("[hook.dll] DetourTransactionBegin failed");
        return 1;
    }

    detourError = DetourUpdateThread(GetCurrentThread());
    if (detourError != NO_ERROR)
    {
        DebugOutA("[hook.dll] DetourUpdateThread failed");
        DetourTransactionAbort();
        return 1;
    }

    detourError = DetourAttach((PVOID*)&Real_CWbemGet, Hook_CWbemGet);
    if (detourError != NO_ERROR)
    {
        DebugOutA("[hook.dll] DetourAttach failed");
        DetourTransactionAbort();
        return 1;
    }

    detourError = DetourTransactionCommit();
    if (detourError != NO_ERROR)
    {
        DebugOutA("[hook.dll] DetourTransactionCommit failed");
        return 1;
    }

    InterlockedExchange(&g_hookInstalled, 1);
    DebugOutA("[hook.dll] hook installed");
    return 0;
}

static void RemoveHook(void)
{
    LONG detourError;

    if (InterlockedCompareExchange(&g_hookInstalled, 0, 1) != 1 || Real_CWbemGet == NULL)
    {
        return;
    }

    detourError = DetourTransactionBegin();
    if (detourError != NO_ERROR)
    {
        DebugOutA("[hook.dll] DetourTransactionBegin(detach) failed");
        return;
    }

    detourError = DetourUpdateThread(GetCurrentThread());
    if (detourError != NO_ERROR)
    {
        DebugOutA("[hook.dll] DetourUpdateThread(detach) failed");
        DetourTransactionAbort();
        return;
    }

    detourError = DetourDetach((PVOID*)&Real_CWbemGet, Hook_CWbemGet);
    if (detourError != NO_ERROR)
    {
        DebugOutA("[hook.dll] DetourDetach failed");
        DetourTransactionAbort();
        return;
    }

    detourError = DetourTransactionCommit();
    if (detourError != NO_ERROR)
    {
        DebugOutA("[hook.dll] DetourTransactionCommit(detach) failed");
        return;
    }

    if (g_fastproxModule != NULL)
    {
        FreeLibrary(g_fastproxModule);
        g_fastproxModule = NULL;
    }
}

BOOL WINAPI DllMain(HINSTANCE hInst, DWORD reason, LPVOID reserved)
{
    HANDLE threadHandle;

    (void)reserved;

    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(hInst);
        threadHandle = CreateThread(NULL, 0, InstallHookThread, NULL, 0, NULL);
        if (threadHandle != NULL)
        {
            CloseHandle(threadHandle);
        }
        else
        {
            DebugOutA("[hook.dll] CreateThread failed");
        }
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
        RemoveHook();
    }

    return TRUE;
}
