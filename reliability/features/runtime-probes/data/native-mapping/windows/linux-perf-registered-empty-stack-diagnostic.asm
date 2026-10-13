; Assembly listing for method CpuWorkload:Run():long (FullOpts)
; Emitting BLENDED_CODE for generic X64 + VEX on Windows
; FullOpts code
; optimized code
; rsp based frame
; fully interruptible
; No PGO data

G_M000_IG01:                ;; offset=0x0000
       push     rdi
       push     rsi
       push     rbx
       sub      rsp, 32
 
G_M000_IG02:                ;; offset=0x0007
       mov      ebx, 0x400
       call     [CpuWorkload:Probe()]
       xor      esi, esi
       xor      edi, edi
       jmp      SHORT G_M000_IG04
 
G_M000_IG03:                ;; offset=0x0018
       movsxd   rax, edi
       add      rsi, rax
       inc      edi
 
G_M000_IG04:                ;; offset=0x0020
       dec      ebx
       jne      SHORT G_M000_IG06
 
G_M000_IG05:                ;; offset=0x0024
       call     [CpuWorkload:Probe()]
       mov      ebx, 0x400
 
G_M000_IG06:                ;; offset=0x002F
       cmp      edi, 0x2710
       jl       SHORT G_M000_IG03
 
G_M000_IG07:                ;; offset=0x0037
       mov      rax, rsi
 
G_M000_IG08:                ;; offset=0x003A
       add      rsp, 32
       pop      rbx
       pop      rsi
       pop      rdi
       ret      
 
; Total bytes of code 66

