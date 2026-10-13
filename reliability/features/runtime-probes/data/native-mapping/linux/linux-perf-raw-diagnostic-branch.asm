; Assembly listing for method CpuWorkload:Run():long (FullOpts)
; Emitting BLENDED_CODE for generic X64 + VEX on Unix
; FullOpts code
; optimized code
; rbp based frame
; fully interruptible
; No PGO data

G_M000_IG01:                ;; offset=0x0000
       push     rbp
       push     r15
       push     r14
       push     rbx
       push     rax
       lea      rbp, [rsp+0x20]
 
G_M000_IG02:                ;; offset=0x000C
       mov      ebx, 0x400
       call     [CpuWorkload:Probe()]
       xor      r15d, r15d
       xor      r14d, r14d
       jmp      SHORT G_M000_IG04
 
G_M000_IG03:                ;; offset=0x001F
       movsxd   rax, r14d
       add      r15, rax
       inc      r14d
 
G_M000_IG04:                ;; offset=0x0028
       dec      ebx
       jne      SHORT G_M000_IG06
 
G_M000_IG05:                ;; offset=0x002C
       call     [CpuWorkload:Probe()]
       mov      ebx, 0x400
 
G_M000_IG06:                ;; offset=0x0037
       cmp      r14d, 0x2710
       jl       SHORT G_M000_IG03
 
G_M000_IG07:                ;; offset=0x0040
       mov      rax, r15
 
G_M000_IG08:                ;; offset=0x0043
       add      rsp, 8
       pop      rbx
       pop      r14
       pop      r15
       pop      rbp
       ret      
 
; Total bytes of code 78

