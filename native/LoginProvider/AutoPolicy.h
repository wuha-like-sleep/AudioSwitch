#pragma once
#include "BridgeProtocol.h"
namespace Bridge {
struct AutoPolicy {
    DWORD session = 0;
    bool attempted = false, armed = false;
    DWORD misses = 0; uint64_t awaySince = 0, completed = 0, phone = 0;
    LONG epoch = 0;
    BYTE ticket[32]{};
    void Absent(uint64_t now) { if (!misses) awaySince = now; ++misses; if (misses >= 3 && now >= awaySince && now - awaySince >= 12000) armed = true; completed = 0; SecureZeroMemory(ticket, sizeof(ticket)); }
    bool Present(const BYTE (&nonce)[32], uint64_t now, uint64_t binding, LONG generation) { misses = 0; if (attempted || !armed) return false; memcpy(ticket, nonce, sizeof(ticket)); completed = now; phone = binding; epoch = generation; return true; }
    bool Commit(const BYTE (&proof)[32], uint64_t now, uint64_t binding, LONG generation) { if (binding != phone || generation != epoch || attempted || !armed || !completed || now < completed || now - completed > 2000 || memcmp(ticket, proof, sizeof(ticket))) return false; attempted = true; SecureZeroMemory(ticket, sizeof(ticket)); return true; }
};
}
