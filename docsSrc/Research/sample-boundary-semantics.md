# Sample boundaries in Coz and JCoz

The inspected implementations gate credit on experiment state when they process a sample.
Neither implements CozNet's source-time membership diagnostic.
This comparison uses native Coz `256e3c389424d6a1691ee5d18b5ecb5a6c1001ec` and JCoz `1df02b0f6e319bd7df05ba5451b1814791d1849f`.

## Native Coz

The Linux sampler requests `PERF_SAMPLE_IP | PERF_SAMPLE_CALLCHAIN`, without `PERF_SAMPLE_TIME`.
The configured stream cannot filter experiment membership by source timestamp. [1]
The perf iterator snapshots ring head and tail at construction. It advances the tail after batch consumption. [2]

`process_samples` maps each buffered record and checks current `_experiment_active`.
A selected match receives current `_delay_size` credit. The function calls `add_delays` after the batch. [3]
Experiment start saves progress, time, selected sample count, and global delay before activation.
That sequence has no ring flush or timestamp admission check.
A buffered event from before activation can therefore receive credit when processed during the experiment. [4]

At the end, Coz saves global delay and time before calculating and writing results.
It clears the active flag later. Processing can add credit after the timing snapshot while the flag remains true.
Such increments do not change the saved `end_global_delay`. This sequence is not an atomic freeze across samples, credit, delay, and progress. [5]
After deactivation, buffered records no longer add credit to that experiment. [3]

## JCoz

The controller activates the experiment before setting delay and reading start time.
It repeatedly sends `SIGPROF`. At the nominal end, it sleeps, clears `in_experiment`, signals threads, and sleeps again.
It then reads `expEnd`, saves global delay and progress, and resets global delay. [6]

The handler captures the stack with ASGCT, then checks `in_experiment`.
Its active branch credits a matching frame. Its inactive branch resets local delay and collects frames for selection. [7]
A delayed signal uses state observed by the handler. It does not use controller dispatch time or a source-event-time window.
The sleeps and signals do not establish an acknowledged barrier across all handlers.

## CozNet consequence

Keep source-time membership, frozen-prefix acceptance, and recorded credit separate.
Source-inside observations outside the frozen prefix receive no retrospective credit.
Accepted observations outside the source-time window remain evidence of boundary mismatch.
The upstream code supplies no tighter source-time contract to import.
It also supplies no proof that this mismatch is negligible for a short managed experiment.

The [bound membership report](../Reference/bound-sample-membership.md) measures these differences under explicit clock assumptions.
It does not qualify selected CPU attribution or prediction accuracy.
See [signal cadence](signal-sample-cadence.md) for the broader sample-credit comparison.

[1]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L598-L616
[2]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/perf.h#L180-L198
[3]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L736-L758
[4]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L277-L325
[5]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L359-L477
[6]: https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L198-L245
[7]: https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L573-L666
