// Copyright (c) 2026 wuha-like-sleep. Licensed under the MIT license.
#define SECURITY_WIN32
#include <windows.h>
#include <security.h>
#include <credentialprovider.h>
#include <wincred.h>
#include <propkey.h>
#include <shlwapi.h>
#include <wrl/client.h>
#include <iostream>
#include <stdexcept>
#include <algorithm>
#include "ProviderId.h"
using Microsoft::WRL::ComPtr;
void Check(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
class User final : public ICredentialProviderUser {
    LONG refs = 1;
    bool msa;
public:
    explicit User(bool isMsa) : msa(isMsa) {}
    IFACEMETHODIMP QueryInterface(REFIID iid, void** value) override { *value = nullptr; if (iid != IID_IUnknown && iid != IID_ICredentialProviderUser) return E_NOINTERFACE; *value = this; AddRef(); return S_OK; }
    IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&refs); }
    IFACEMETHODIMP_(ULONG) Release() override { auto n = InterlockedDecrement(&refs); if (!n) delete this; return n; }
    IFACEMETHODIMP GetSid(PWSTR* value) override { return SHStrDupW(L"S-1-5-21-100-200-300-1001", value); }
    IFACEMETHODIMP GetProviderID(GUID* value) override { *value = GUID_NULL; return S_OK; }
    IFACEMETHODIMP GetStringValue(REFPROPERTYKEY key, PWSTR* value) override {
        if (IsEqualPropertyKey(key, PKEY_Identity_QualifiedUserName)) return SHStrDupW(msa ? L"MicrosoftAccount\\audioswitch-test@example.invalid" : L"TESTPC\\local-test", value);
        *value = nullptr; return E_NOTIMPL;
    }
    IFACEMETHODIMP GetValue(REFPROPERTYKEY, PROPVARIANT*) override { return E_NOTIMPL; }
};
class Users final : public ICredentialProviderUserArray {
    LONG refs = 1;
public:
    IFACEMETHODIMP QueryInterface(REFIID iid, void** value) override { *value = nullptr; if (iid != IID_IUnknown && iid != IID_ICredentialProviderUserArray) return E_NOINTERFACE; *value = this; AddRef(); return S_OK; }
    IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&refs); }
    IFACEMETHODIMP_(ULONG) Release() override { auto n = InterlockedDecrement(&refs); if (!n) delete this; return n; }
    IFACEMETHODIMP SetProviderFilter(REFGUID) override { return S_OK; }
    IFACEMETHODIMP GetAccountOptions(CREDENTIAL_PROVIDER_ACCOUNT_OPTIONS* value) override { *value = CPAO_NONE; return S_OK; }
    IFACEMETHODIMP GetCount(DWORD* value) override { *value = 2; return S_OK; }
    IFACEMETHODIMP GetAt(DWORD index, ICredentialProviderUser** value) override { if (index > 1) return E_INVALIDARG; *value = new User(index == 0); return S_OK; }
};
void Verify(IClassFactory* factory) {
    ComPtr<ICredentialProvider> provider;
    Check(SUCCEEDED(factory->CreateInstance(nullptr, IID_PPV_ARGS(&provider))), "Create provider");
    Check(provider->SetUsageScenario(CPUS_CREDUI, 0) == E_NOTIMPL, "Reject unrelated CredUI");
    Check(SUCCEEDED(provider->SetUsageScenario(CPUS_LOGON, 0)), "Logon scenario");
    ComPtr<ICredentialProviderSetUserArray> setUsers; Check(SUCCEEDED(provider.As(&setUsers)), "V2 user array");
    ComPtr<ICredentialProviderUserArray> users; users.Attach(new Users());
    Check(SUCCEEDED(setUsers->SetUserArray(users.Get())), "Set users");
    for (auto scenario : {CPUS_LOGON, CPUS_UNLOCK_WORKSTATION}) {
        Check(SUCCEEDED(provider->SetUsageScenario(scenario, 0)), "Set scenario");
        DWORD count = 999, selected = 0; BOOL automatic = TRUE;
        Check(SUCCEEDED(provider->GetCredentialCount(&count, &selected, &automatic)), "Enumerate");
        Check(count == 1 && selected == CREDENTIAL_PROVIDER_NO_DEFAULT && !automatic, "MSA only; no default, no autologon");
        DWORD fields = 0; Check(SUCCEEDED(provider->GetFieldDescriptorCount(&fields)) && fields == 4, "Fields");
        for (DWORD id = 0; id < fields; ++id) {
            CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR* descriptor = nullptr;
            Check(SUCCEEDED(provider->GetFieldDescriptorAt(id, &descriptor)), "Descriptor");
            Check(descriptor && descriptor->dwFieldID == id && descriptor->pszLabel, "Descriptor content");
            CoTaskMemFree(descriptor->pszLabel); CoTaskMemFree(descriptor);
        }
        ComPtr<ICredentialProviderCredential> credential;
        Check(SUCCEEDED(provider->GetCredentialAt(0, &credential)), "Credential");
        ComPtr<ICredentialProviderCredential2> v2; Check(SUCCEEDED(credential.As(&v2)), "V2 credential");
        PWSTR sid = nullptr; Check(SUCCEEDED(v2->GetUserSid(&sid)) && wcscmp(sid, L"S-1-5-21-100-200-300-1001") == 0, "SID binding"); CoTaskMemFree(sid);
        Check(SUCCEEDED(credential->SetSelected(&automatic)) && !automatic, "Selection cannot autologon");
        CREDENTIAL_PROVIDER_GET_SERIALIZATION_RESPONSE response{};
        CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION serialization{}; PWSTR status = nullptr; CREDENTIAL_PROVIDER_STATUS_ICON icon{};
        Check(SUCCEEDED(credential->GetSerialization(&response, &serialization, &status, &icon)), "Empty password result");
        Check(response == CPGSR_NO_CREDENTIAL_NOT_FINISHED && !serialization.rgbSerialization && status, "Empty password rejected"); CoTaskMemFree(status);
        constexpr wchar_t testPassword[] = L"test-only-not-a-real-password-739!";
        Check(SUCCEEDED(credential->SetStringValue(2, testPassword)), "Test password set");
        Check(SUCCEEDED(credential->GetSerialization(&response, &serialization, &status, &icon)), "Serialize identity-provider credential");
        Check(response == CPGSR_RETURN_CREDENTIAL_FINISHED && serialization.rgbSerialization && serialization.cbSerialization && serialization.clsidCredentialProvider == AudioSwitchLoginProviderId, "Serialized metadata");
        auto start = reinterpret_cast<const BYTE*>(testPassword);
        auto end = start + sizeof(testPassword) - sizeof(wchar_t);
        Check(std::search(serialization.rgbSerialization, serialization.rgbSerialization + serialization.cbSerialization, start, end) == serialization.rgbSerialization + serialization.cbSerialization, "Serialized buffer contains no plaintext test password");
        // Identity-provider credentials unpack to marshaled strings, not a plaintext password.
        // Validate the EX2 structure and inspect a separate, explicitly decrypted SSPI copy.
        auto original = reinterpret_cast<PSEC_WINNT_AUTH_IDENTITY_OPAQUE>(serialization.rgbSerialization);
        Check(SspiValidateAuthIdentity(original) == SEC_E_OK, "Valid SSPI identity");
        PSEC_WINNT_AUTH_IDENTITY_OPAQUE decrypted = nullptr;
        Check(SspiCopyAuthIdentity(original, &decrypted) == SEC_E_OK, "Copy protected identity");
        Check(SspiIsAuthIdentityEncrypted(decrypted) != FALSE, "Identity is encrypted");
        Check(SspiDecryptAuthIdentityEx(SEC_WINNT_AUTH_IDENTITY_ENCRYPT_SAME_LOGON, decrypted) == SEC_E_OK, "Decrypt same-session test identity");
        ULONG decodedSize = 0; char* decoded = nullptr;
        Check(SspiMarshalAuthIdentity(decrypted, &decodedSize, &decoded) == SEC_E_OK, "Marshal decrypted test identity");
        const auto decodedBytes = reinterpret_cast<const BYTE*>(decoded);
        Check(std::search(decodedBytes, decodedBytes + decodedSize, start, end) != decodedBytes + decodedSize, "Recovered exact test password from protected identity");
        SecureZeroMemory(decoded, decodedSize); SspiLocalFree(decoded);
        SspiZeroAuthIdentity(decrypted); SspiFreeAuthIdentity(decrypted);
        SecureZeroMemory(serialization.rgbSerialization, serialization.cbSerialization); CoTaskMemFree(serialization.rgbSerialization); CoTaskMemFree(status);
        PWSTR remaining = nullptr; Check(SUCCEEDED(credential->GetStringValue(2, &remaining)) && !*remaining, "Password cleared after serialization"); CoTaskMemFree(remaining);
        Check(SUCCEEDED(credential->SetStringValue(2, testPassword)), "Set before deselect"); credential->SetDeselected();
        credential->GetStringValue(2, &remaining); Check(!*remaining, "Deselect clears password"); CoTaskMemFree(remaining);
        credential->SetStringValue(2, testPassword); status = nullptr;
        Check(SUCCEEDED(credential->ReportResult(static_cast<NTSTATUS>(0xC000006D), 0, &status, &icon)) && status && icon == CPSI_ERROR, "Failure advises system fallback"); CoTaskMemFree(status);
        credential->GetStringValue(2, &remaining); Check(!*remaining, "Failure clears password"); CoTaskMemFree(remaining);
    }
}
int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 2;
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    auto library = LoadLibraryW(argv[1]); if (!library) return 3;
    using GetFactory = HRESULT(__stdcall*)(REFCLSID, REFIID, void**);
    using CanUnload = HRESULT(__stdcall*)();
    auto getFactory = reinterpret_cast<GetFactory>(GetProcAddress(library, "DllGetClassObject"));
    auto canUnload = reinterpret_cast<CanUnload>(GetProcAddress(library, "DllCanUnloadNow"));
    int result = 0;
    try {
        Check(getFactory && canUnload && canUnload() == S_OK, "Exports and initial unload");
        {
            ComPtr<IClassFactory> factory; Check(SUCCEEDED(getFactory(AudioSwitchLoginProviderId, IID_PPV_ARGS(&factory))), "Class factory");
            Check(canUnload() == S_FALSE, "Live object prevents unload");
            Verify(factory.Get());
        }
        Check(canUnload() == S_OK, "No leaked COM objects");
        std::cout << "Login provider COM, MSA/SID binding, protected serialization, password clearing and system fallback checks passed. No real account authentication performed.\n";
    } catch (const std::exception& error) { std::cerr << error.what() << " (Win32 " << GetLastError() << ")\n"; result = 1; }
    FreeLibrary(library); CoUninitialize(); return result;
}
