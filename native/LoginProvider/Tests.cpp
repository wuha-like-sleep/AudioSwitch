#include "LoginDiagnostics.h"
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
#include "BridgeProtocol.h"
#include "CredentialVault.h"
#include "AutoPolicy.h"
#include <sddl.h>
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
class Events final : public ICredentialProviderEvents {
    LONG refs = 1;
public:
    LONG calls = 0;
    IFACEMETHODIMP QueryInterface(REFIID iid, void** value) override { *value = nullptr; if (iid != IID_IUnknown && iid != IID_ICredentialProviderEvents) return E_NOINTERFACE; *value = this; AddRef(); return S_OK; }
    IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&refs); }
    IFACEMETHODIMP_(ULONG) Release() override { auto n = InterlockedDecrement(&refs); if (!n) delete this; return n; }
    IFACEMETHODIMP CredentialsChanged(UINT_PTR) override { InterlockedIncrement(&calls); return S_OK; }
};
void Verify(IClassFactory* factory, bool denyBound) {
    ComPtr<ICredentialProvider> provider;
    Check(SUCCEEDED(factory->CreateInstance(nullptr, IID_PPV_ARGS(&provider))), "Create provider");
    Check(provider->SetUsageScenario(CPUS_CREDUI, 0) == E_NOTIMPL, "Reject unrelated CredUI");
    Check(SUCCEEDED(provider->SetUsageScenario(CPUS_LOGON, 0)), "Logon scenario");
    ComPtr<ICredentialProviderSetUserArray> setUsers; Check(SUCCEEDED(provider.As(&setUsers)), "V2 user array");
    ComPtr<ICredentialProviderUserArray> users; users.Attach(new Users());
    Check(SUCCEEDED(setUsers->SetUserArray(users.Get())), "Set users");
    ComPtr<Events> events; events.Attach(new Events());
    Check(SUCCEEDED(provider->Advise(events.Get(), 47)), "Advise native automatic worker");
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
        Check(SUCCEEDED(credential->ReportResult(static_cast<NTSTATUS>(0xC000006D), static_cast<NTSTATUS>(0xC000006A), nullptr, nullptr)), "ReportResult accepts optional null outputs");
        CREDENTIAL_PROVIDER_GET_SERIALIZATION_RESPONSE response{};
        CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION serialization{}; PWSTR status = nullptr; CREDENTIAL_PROVIDER_STATUS_ICON icon{};
        Check(SUCCEEDED(credential->GetSerialization(&response, &serialization, &status, &icon)), "Empty password result");
        Check(response == CPGSR_NO_CREDENTIAL_NOT_FINISHED && !serialization.rgbSerialization && status, "Empty password rejected"); CoTaskMemFree(status);
        constexpr wchar_t testPassword[] = L"test-only-not-a-real-password-739!";
        Check(SUCCEEDED(credential->SetStringValue(2, testPassword)), "Test password set");
        Check(SUCCEEDED(credential->GetSerialization(&response, &serialization, &status, &icon)), "Serialize identity-provider credential");
        if (denyBound) {
            Check(response == CPGSR_NO_CREDENTIAL_NOT_FINISHED && !serialization.rgbSerialization && status && icon == CPSI_WARNING, "Bound phone without valid broker proof cannot serialize");
            CoTaskMemFree(status);
            PWSTR cleared = nullptr; credential->GetStringValue(2, &cleared); Check(cleared && !*cleared, "Rejected bridge clears password"); CoTaskMemFree(cleared);
            continue;
        }
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
        // The Ex entry point is exported by SspiCli, not the Secur32 import library.
        auto sspi = LoadLibraryExW(L"sspicli.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        using DecryptIdentity = SECURITY_STATUS(SEC_ENTRY*)(ULONG, PSEC_WINNT_AUTH_IDENTITY_OPAQUE);
        auto decrypt = sspi ? reinterpret_cast<DecryptIdentity>(GetProcAddress(sspi, "SspiDecryptAuthIdentityEx")) : nullptr;
        Check(decrypt != nullptr, "System SSPI decrypt entry point");
        const auto decryptedStatus = decrypt(SEC_WINNT_AUTH_IDENTITY_ENCRYPT_SAME_LOGON, decrypted);
        FreeLibrary(sspi);
        Check(decryptedStatus == SEC_E_OK, "Decrypt same-session test identity");
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
    Check(SUCCEEDED(provider->UnAdvise()), "Cancel automatic worker");
    Check(events->calls == 0, "No automatic callback without an authenticated phone proof");
}
void VerifyVault() {
    constexpr wchar_t testSid[] = L"S-1-5-21-100-200-300-1001";
    constexpr wchar_t bindingPath[] = L"SOFTWARE\\AudioSwitch\\LoginBridge\\Bindings\\S-1-5-21-100-200-300-1001";
    HKEY binding = nullptr;
    Check(RegCreateKeyExW(HKEY_LOCAL_MACHINE, bindingPath, 0, nullptr, 0, KEY_ALL_ACCESS | KEY_WOW64_64KEY, nullptr, &binding, nullptr) == ERROR_SUCCESS, "Create isolated vault binding");
    constexpr wchar_t phone[] = L"123456789ABC";
    RegSetValueExW(binding, L"Phone", 0, REG_SZ, reinterpret_cast<const BYTE*>(phone), sizeof(phone)); RegCloseKey(binding);
    Check(RegOpenKeyExW(HKEY_LOCAL_MACHINE, bindingPath, 0, KEY_SET_VALUE | KEY_WOW64_64KEY, &binding) == ERROR_SUCCESS, "Open isolated binding writer");
    wchar_t paddedPhone[14] = L"123456789ABC";
    Check(RegSetValueExW(binding, L"Phone", 0, REG_SZ, reinterpret_cast<const BYTE*>(paddedPhone), sizeof(paddedPhone)) == ERROR_SUCCESS, "Write zero padded phone fixture");
    uint64_t parsedPhone = 0;
    Check(Bridge::ReadBinding(testSid, &parsedPhone) && parsedPhone == 0x123456789ABC, "Equivalent registry string padding accepted");
    paddedPhone[13] = L'F';
    RegSetValueExW(binding, L"Phone", 0, REG_SZ, reinterpret_cast<const BYTE*>(paddedPhone), sizeof(paddedPhone));
    Check(Bridge::ReadBinding(testSid, &parsedPhone) && parsedPhone == 0, "Nonzero registry suffix rejected");
    RegSetValueExW(binding, L"Phone", 0, REG_SZ, reinterpret_cast<const BYTE*>(phone), sizeof(phone)); RegCloseKey(binding);
    Vault::Record input; wcscpy_s(input.sid, testSid); wcscpy_s(input.qualified, L"MicrosoftAccount\\audioswitch-test@example.invalid"); wcscpy_s(input.password, L"vault-test-only-47!"); input.phone = 0x123456789ABC;
    Check(Vault::Save(input) && Vault::Enabled(testSid), "Save DPAPI vault and opt-in marker");
    LoginDiagnostics::Write(testSid, L"WindowsResult", 0, 0xC000006D, 0xC000006A);
    Check(RegOpenKeyExW(HKEY_LOCAL_MACHINE, bindingPath, 0, KEY_QUERY_VALUE | KEY_WOW64_64KEY, &binding) == ERROR_SUCCESS, "Open diagnostic metadata");
    LoginDiagnostics::Record diagnostic; DWORD diagnosticBytes = sizeof(diagnostic), diagnosticType = 0;
    Check(RegQueryValueExW(binding, L"WindowsResult", nullptr, &diagnosticType, reinterpret_cast<BYTE*>(&diagnostic), &diagnosticBytes) == ERROR_SUCCESS && diagnosticType == REG_BINARY && diagnosticBytes == 24 && diagnostic.version == 1 && diagnostic.result == 0 && diagnostic.error == 0xC000006D && diagnostic.substatus == 0xC000006A && diagnostic.utc != 0, "Diagnostic preserves authentication failure and substatus without secrets");
    RegCloseKey(binding);
    Vault::Record output;
    Check(Vault::Load(testSid, input.qualified, output) && wcscmp(input.password, output.password) == 0, "Vault protected roundtrip");
    Check(!Vault::Load(testSid, L"MicrosoftAccount\\other@example.invalid", output), "Wrong account cannot load vault");
    HKEY secretKey = nullptr; constexpr wchar_t path[] = L"SOFTWARE\\AudioSwitch\\LoginBridge\\Vault\\S-1-5-21-100-200-300-1001";
    Check(RegOpenKeyExW(HKEY_LOCAL_MACHINE, path, 0, KEY_ALL_ACCESS | KEY_WOW64_64KEY, &secretKey) == ERROR_SUCCESS, "Open isolated vault key");
    BYTE encrypted[16384]{}; DWORD bytes = sizeof(encrypted), type = 0;
    Check(RegQueryValueExW(secretKey, L"Secret", nullptr, &type, encrypted, &bytes) == ERROR_SUCCESS, "Read encrypted test secret");
    const auto first = reinterpret_cast<const BYTE*>(input.password); const auto last = first + wcslen(input.password) * sizeof(wchar_t);
    Check(std::search(encrypted, encrypted + bytes, first, last) == encrypted + bytes, "Vault has no plaintext test password");
    HANDLE processToken = nullptr, restricted = nullptr;
    Check(OpenProcessToken(GetCurrentProcess(), TOKEN_DUPLICATE | TOKEN_QUERY, &processToken) != FALSE, "Open test token");
    PSID users = nullptr; Check(ConvertStringSidToSidW(L"S-1-5-32-545", &users) != FALSE, "Users SID"); SID_AND_ATTRIBUTES restriction{users, 0};
    Check(CreateRestrictedToken(processToken, DISABLE_MAX_PRIVILEGE, 0, nullptr, 0, nullptr, 1, &restriction, &restricted) != FALSE, "Restricted test token");
    Check(ImpersonateLoggedOnUser(restricted) != FALSE, "Restricted impersonation");
    HKEY denied = nullptr; const auto read = RegOpenKeyExW(HKEY_LOCAL_MACHINE, path, 0, KEY_QUERY_VALUE | KEY_WOW64_64KEY, &denied);
    RevertToSelf(); if (denied) RegCloseKey(denied); CloseHandle(restricted); CloseHandle(processToken); LocalFree(users);
    Check(read == ERROR_ACCESS_DENIED, "Restricted user cannot read vault ciphertext");
    encrypted[bytes / 2] ^= 1; RegSetValueExW(secretKey, L"Secret", 0, REG_BINARY, encrypted, bytes); RegCloseKey(secretKey); SecureZeroMemory(encrypted, sizeof(encrypted));
    Check(!Vault::Load(testSid, input.qualified, output), "Tampered vault rejected");
    Vault::Revoke(testSid); Check(!Vault::Enabled(testSid) && !Vault::Load(testSid, input.qualified, output), "Revocation disables loading");
    RegDeleteKeyExW(HKEY_LOCAL_MACHINE, bindingPath, KEY_WOW64_64KEY, 0);
    std::cout << "DPAPI roundtrip, restricted registry access, identity mismatch, ciphertext tampering and revocation checks passed with fictional credentials.\n";
}
void VerifyAutoPolicy() {
    Check(Bridge::CanArmAbsence(10060, Bridge::PhoneStage::Wait), "Radio timeout can count as absence");
    Check(!Bridge::CanArmAbsence(10013, Bridge::PhoneStage::Connect) && !Bridge::CanArmAbsence(10060, Bridge::PhoneStage::Encryption) && !Bridge::CanArmAbsence(ERROR_NOT_FOUND, Bridge::PhoneStage::Pairing), "Authentication, local configuration and missing pairing cannot arm unlock");
    BYTE nonce[32]{}; nonce[0] = 1;
    Bridge::AutoPolicy policy;
    Check(!policy.Present(nonce, 1000, 7, 2), "Phone initially present cannot immediately unlock");
    policy.Absent(1000); policy.Absent(5000); Check(!policy.Present(nonce, 6000, 7, 2), "Short departure cannot arm");
    policy.Absent(10000); policy.Absent(16000); policy.Absent(23000);
    Check(policy.Present(nonce, 24000, 7, 2), "Sustained failed checks followed by return can arm");
    Check(!policy.Commit(nonce, 24001, 8, 2) && !policy.Commit(nonce, 24001, 7, 3), "Other binding or lock generation rejected");
    Check(!policy.Commit(nonce, 26001, 7, 2), "Expired automatic approval rejected");
    Check(policy.Present(nonce, 27000, 7, 2) && policy.Commit(nonce, 27001, 7, 2), "Fresh proof commits once");
    Check(!policy.Commit(nonce, 27002, 7, 2) && !policy.Present(nonce, 28000, 7, 2), "Same lock cannot automatically retry");
}
int wmain(int argc, wchar_t** argv) {
    if (argc != 2 && argc != 3) return 2;
    if (argc == 3 && wcscmp(argv[2], L"--check-binding") == 0) {
        uint64_t phone = 0;
        const bool found = Bridge::ReadBinding(L"S-1-5-21-100-200-300-1001", &phone);
        std::cout << "Synthetic binding: present=" << found << " valid=" << (phone == 0x123456789ABC) << "\n";
        return found && phone == 0x123456789ABC ? 0 : 4;
    }
    const bool denyBound = argc == 3 && wcscmp(argv[2], L"--deny-bound") == 0;
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    auto library = LoadLibraryW(argv[1]); if (!library) return 3;
    using GetFactory = HRESULT(__stdcall*)(REFCLSID, REFIID, void**);
    using CanUnload = HRESULT(__stdcall*)();
    auto getFactory = reinterpret_cast<GetFactory>(GetProcAddress(library, "DllGetClassObject"));
    auto canUnload = reinterpret_cast<CanUnload>(GetProcAddress(library, "DllCanUnloadNow"));
    int result = 0;
    try {
        VerifyAutoPolicy();
        if (argc == 3 && wcscmp(argv[2], L"--vault-tests") == 0) VerifyVault();
        Check(getFactory && canUnload && canUnload() == S_OK, "Exports and initial unload");
        {
            ComPtr<IClassFactory> factory; Check(SUCCEEDED(getFactory(AudioSwitchLoginProviderId, IID_PPV_ARGS(&factory))), "Class factory");
            Check(canUnload() == S_FALSE, "Live object prevents unload");
            Verify(factory.Get(), denyBound);
        }
        Check(canUnload() == S_OK, "No leaked COM objects");
        Bridge::Request request; request.session = 2; request.nonce[0] = 1; wcscpy_s(request.sid, L"S-1-5-21-100-200-300-1001");
        request.intent = Bridge::Intent::ServiceProbe;
        Check(Bridge::ValidRequest(request), "Explicit service diagnostic request supported");
        request.payloadBytes = 1; Check(!Bridge::ValidRequest(request), "Service probe cannot carry credentials"); request.payloadBytes = 0;
        request.intent = Bridge::Intent::Manual;
        Bridge::Response reply; reply.result = Bridge::Result::Verified; reply.session = request.session; reply.phone = 0x123456789ABC; reply.completed = 10000; memcpy(reply.nonce, request.nonce, sizeof(reply.nonce));
        Check(Bridge::ValidResponse(request, reply, reply.phone, 11000), "Fresh proof accepted");
        Check(!Bridge::ValidResponse(request, reply, reply.phone, 12001), "Expired proof rejected");
        Check(!Bridge::ValidResponse(request, reply, reply.phone, 9999), "Future proof rejected");
        Check(!Bridge::ValidResponse(request, reply, reply.phone + 1, 11000), "Different phone rejected");
        reply.nonce[0] ^= 1; Check(!Bridge::ValidResponse(request, reply, reply.phone, 11000), "Old request nonce rejected"); reply.nonce[0] ^= 1;
        reply.session++; Check(!Bridge::ValidResponse(request, reply, reply.phone, 11000), "Other session rejected"); reply.session--;
        reply.version++; Check(!Bridge::ValidResponse(request, reply, reply.phone, 11000), "Unknown protocol rejected");
        if (denyBound) std::cout << "Bound-phone gate rejected a non-LogonUI or missing broker, with no credential serialization.\n";
        std::cout << "Login provider COM, MSA/SID binding, protected serialization, password clearing and system fallback checks passed. No real account authentication performed.\n";
    } catch (const std::exception& error) { std::cerr << error.what() << " (Win32 " << GetLastError() << ")\n"; result = 1; }
    FreeLibrary(library); CoUninitialize(); return result;
}
