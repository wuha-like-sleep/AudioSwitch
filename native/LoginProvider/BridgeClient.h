#pragma once
#include "BridgeProtocol.h"
struct PhoneApproval { BYTE nonce[32]{}; uint64_t completed = 0; };
bool VerifyBoundPhone(PCWSTR sid, HANDLE cancel = nullptr);
bool ProbeAutomatic(PCWSTR sid, PhoneApproval& approval, HANDLE cancel);
bool CommitAutomatic(PCWSTR sid, const PhoneApproval& approval);
DWORD EnrollLocalCredential(PCWSTR sid);
