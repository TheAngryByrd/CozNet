; Assembly listing for method CozNet.Instrumentation.Workload:Work(int):long (FullOpts)
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
       push     r13
       push     rbx
       lea      rbp, [rsp+0x20]
       mov      ebx, edi
 
G_M000_IG02:                ;; offset=0x000F
       mov      r15d, 0x400
       call     [CozNet.Instrumentation.Workload:Probe()]
       xor      r14d, r14d
       xor      r13d, r13d
       jmp      SHORT G_M000_IG04
 
G_M000_IG03:                ;; offset=0x0023
       movsxd   rax, r13d
       add      r14, rax
       inc      r13d
 
G_M000_IG04:                ;; offset=0x002C
       dec      r15d
       jne      SHORT G_M000_IG06
 
G_M000_IG05:                ;; offset=0x0031
       call     [CozNet.Instrumentation.Workload:Probe()]
       mov      r15d, 0x400
 
G_M000_IG06:                ;; offset=0x003D
       cmp      r13d, ebx
       jl       SHORT G_M000_IG03
 
G_M000_IG07:                ;; offset=0x0042
       mov      rax, r14
 
G_M000_IG08:                ;; offset=0x0045
       pop      rbx
       pop      r13
       pop      r14
       pop      r15
       pop      rbp
       ret      
 
; Total bytes of code 78

