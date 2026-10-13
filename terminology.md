# Terms

- **Region:** A named, synchronous CPU operation on the experiment thread.
- **Virtual speedup:** The requested fractional reduction in the selected region's execution time.
- **Control:** A run that adds proportional delay to every marked region.
- **Treatment:** A run that adds the same proportional delay except in the selected region.
- **Block:** One randomized set containing a control and treatment for each requested speedup.
- **Progress:** One completed unit of identical work. Each measured run must complete the same number.
- **Elapsed reduction:** `1 - treatment elapsed / control elapsed`, computed within each block.
- **Coverage:** Marked region time divided by elapsed time after injected delays are removed.

Elapsed reduction is not throughput increase. A 20% elapsed reduction implies a 25% throughput increase for fixed work.
