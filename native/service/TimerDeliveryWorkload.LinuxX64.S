.text
.globl coz_timer_workload
.type coz_timer_workload, @function
coz_timer_workload:
.globl coz_timer_workload_begin
coz_timer_workload_begin:
    movl $1, (%rsi)
.Ltimer_loop:
    lock incq (%rdx)
    movl (%rdi), %eax
    testl %eax, %eax
    je .Ltimer_loop
    ret
.globl coz_timer_workload_end
coz_timer_workload_end:
.size coz_timer_workload, .-coz_timer_workload
.section .note.GNU-stack,"",@progbits
