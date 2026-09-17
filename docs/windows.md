# Windows and this program

What Windows says before the first start, and why.

## Smart App Control

Smart App Control is active on this development machine. It blocked the first
build immediately: a normal `dotnet build` produces `mliv.exe` plus `mliv.dll`,
the exe is allowed to start, but loading the unsigned `mliv.dll` is refused by the
code integrity policy.

```
Event 3077 - attempted to load mliv.dll that did not meet the
Enterprise signing level requirements
Policy ID {0283ac0f-fff1-49ae-ada1-8a933130cad6}
```

**What was measured - and what was not.** While the block was active, a
single-file publish went through reliably: without a separate managed DLL there
is nothing to block. Later SAC stopped blocking unsigned builds of its own
accord. Three repetitions with a forced recompile all went through, signed and
unsigned alike.

From which follows the actual problem: **SAC is not rule-based, it is
reputation-based.** Microsoft's Intelligent Security Graph decides per file, and
the same file can be blocked today and allowed tomorrow. Whether signing helped
could therefore not be measured cleanly.

**What was built out of it:**

| Measure | Effect |
|---|---|
| Single-file publish (`scripts/run.ps1`, `scripts/package.ps1`) | Removes the separate managed DLL - the one surface where the block demonstrably applied. |
| Signing on every run (`scripts/sign.ps1`) | The release pipeline stands from the start; later only the certificate is swapped. |
| SAC detection in the diagnostic report | The user learns about it **before** the downgrade, not when the trainer stays mute. |

**No self-deception about signing:** a self-signed certificate does not satisfy
SAC. What is judged is the signer's reputation with the ISG, not the local trust
chain. Deterministically, only a real code-signing certificate with established
reputation solves this - set `MLIV_SIGN_THUMBPRINT`, then the release path in
`sign.ps1` applies.

**For the trainer: measured, and it went wrong.**

| | T0 | T1 |
|---|---|---|
| Size | 147 KB | 193 KB |
| Signature | self-signed | the same |
| SAC state | active (`1`) | active (`1`) |
| Result | **loaded** | **blocked** |

Same machine, same certificate, same policy - a different result. T1 failed with
event 3077 and ASI loader error 4551 (`0x11C7`, the Win32 part of `0x800711C7`).
The dependencies were clean, the ASI x86 and signed. There was technically
nothing to correct.

That establishes what stands above as a suspicion: **SAC is not a rule you can
satisfy.** On this development machine it was therefore switched off.

**For end users this stays unsolved.** Anyone with Smart App Control active will
not get the trainer loaded. Deterministically, only a real code-signing
certificate with established reputation helps there - set
`MLIV_SIGN_THUMBPRINT`, then the release path in `sign.ps1` applies.

**Old state, superseded:** the single-file approach does not help the trainer. An
`.asi` is by definition an unsigned DLL loaded into `GTAIV.exe` - exactly the
operation SAC prevents. This affects every end user with Smart App Control active
as well.

