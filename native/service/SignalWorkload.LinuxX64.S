.text
.globl coz_service_workload
.type coz_service_workload, @function
coz_service_workload:
.globl coz_service_workload_begin
coz_service_workload_begin:
    movl $1, (%rsi)
.Lservice_loop:
    pause
    movl (%rdi), %eax
    testl %eax, %eax
    je .Lservice_loop
    ret
.globl coz_service_workload_end
coz_service_workload_end:
.size coz_service_workload, .-coz_service_workload
.section .note.GNU-stack,"",@progbits
