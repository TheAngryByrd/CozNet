; Assembly listing for method CozNet.Instrumentation.Workload:Work(int):long (FullOpts)
; Emitting BLENDED_CODE for generic X64 + VEX on Windows
; FullOpts code
; optimized code
; rsp based frame
; fully interruptible
; No PGO data

G_M000_IG01:                ;; offset=0x0000
       push     rdi
       push     rsi
       push     rbp
       push     rbx
       sub      rsp, 40
       mov      ebx, ecx
 
G_M000_IG02:                ;; offset=0x000A
       mov      esi, 0x400
       call     [CozNet.Instrumentation.Workload:Probe()]
       xor      edi, edi
       xor      ebp, ebp
       jmp      SHORT G_M000_IG04
 
G_M000_IG03:                ;; offset=0x001B
       movsxd   rax, ebp
       add      rdi, rax
       inc      ebp
 
G_M000_IG04:                ;; offset=0x0023
       dec      esi
       jne      SHORT G_M000_IG06
 
G_M000_IG05:                ;; offset=0x0027
       call     [CozNet.Instrumentation.Workload:Probe()]
       mov      esi, 0x400
 
G_M000_IG06:                ;; offset=0x0032
       cmp      ebp, ebx
       jl       SHORT G_M000_IG03
 
G_M000_IG07:                ;; offset=0x0036
       mov      rax, rdi
 
G_M000_IG08:                ;; offset=0x0039
       add      rsp, 40
       pop      rbx
       pop      rbp
       pop      rsi
       pop      rdi
       ret      
 
; Total bytes of code 66

