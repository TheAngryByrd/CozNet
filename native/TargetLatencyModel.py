"""Deterministic tick model of the target-local transaction pipeline, for estimator checks.

This is a simplified model, not a CozNet execution. See docsSrc/Explanations/target-local-latency-model.md.

Arithmetic: every quantity is an integer tick count. Inexact work or credit is rejected, not rounded.

Order of events at one instant t, an integer tick boundary:
 1. Publishes due at t raise G, the running maximum of the published values.
 2. A fixed point of producer and consumer actions at t: the producer enqueues when its work ended and the queue has space, and begins
    the next transaction at t while t < stop. The consumer dequeues, settles, and stamps End. The loop repeats until no action changes state.
 3. A snapshot at t counts every Begin and every End with a time at or before t.
Then one tick of work or payment runs from t to t + 1. A sample fires when the producer selected CPU reaches phase + k*T, and it adds its
credit to the generated credit of the producer.

Publication policy:
 - immediate: each credit part of a sample is published at t + 1 + its offset, as each CozNet dispatch publishes.
 - release: the generated credit is published only when the producer enqueues, before it takes its token. This is a controlled policy,
   not upstream Coz: upstream also publishes at its 10 ms timer, and the model has no timer.
Generated credit that is not yet published is the unpublished credit. Each snapshot reports it, and a completed run has none.

Domain: payments are exact, so no thread publishes above the producer, and the producer never pays. Payment overshoot is outside the model.
"""
from fractions import Fraction


def _positive_int(value, name):
    if type(value) is not int or value < 1:
        raise ValueError(f"{name} must be an integer of at least 1, not {value!r}")
    return value


def exact_ticks(value, name):
    value = Fraction(value)
    if value.denominator != 1:
        raise ValueError(f"{name} = {value} is not a whole number of ticks")
    return int(value)


def validate(p, c, K, s, T, phase, parts, offsets, stop, real):
    _positive_int(p, "p")
    _positive_int(c, "c")
    _positive_int(K, "K")
    _positive_int(T, "T")
    _positive_int(stop, "stop")
    if type(phase) is not int or not 1 <= phase <= T:
        raise ValueError(f"phase must be an integer from 1 to T, not {phase!r}")
    s = Fraction(s)
    if not 0 <= s < 1:
        raise ValueError(f"speedup must be at least 0 and below 1, not {s}")
    work = exact_ticks(p * (1 - s), "real work") if real else p
    credit = 0 if real else exact_ticks(s * T, "credit")
    if parts is None:
        parts, offsets = [credit], [0] if offsets is None else offsets
    if offsets is None or len(parts) != len(offsets):
        raise ValueError("parts and offsets must have the same length")
    if any(type(x) is not int or x < 0 for x in list(parts) + list(offsets)):
        raise ValueError("parts and offsets must be integers of at least 0")
    if not real and sum(parts) != credit:
        raise ValueError(f"credit parts sum to {sum(parts)}, not s*T = {credit}")
    return work, credit, list(parts), list(offsets)


def simulate(p, c, K, s="0", T=10, phase=1, parts=None, offsets=None, stop=2000, wake="token", real=False, publication="immediate"):
    if publication not in ("immediate", "release"):
        raise ValueError("publication must be immediate or release")
    if publication == "release" and (parts is not None or offsets is not None):
        raise ValueError("credit parts and offsets apply only to immediate publication")
    work, credit, parts, offsets = validate(p, c, K, s, T, phase, parts, offsets, stop, real)
    if wake not in ("token", "global"):
        raise ValueError("wake must be token or global")
    if real:
        parts, offsets = [], []
    G, pending, publishes, snapshots = 0, [], [], []
    queue, tx, begins = [], [], [0]
    acc = {"generated": 0, "published": 0, "skipped": 0, "paid": 0, "producerWork": 0, "consumerWork": 0, "samples": 0}
    prod = {"state": "work", "left": work, "begin": 0, "cpu": 0}
    cons = {"state": "wait", "blocked": True, "local": 0, "left": 0, "item": None, "next": None}
    t = 0
    while True:
        for item in sorted(x for x in pending if x[0] <= t):
            pending.remove(item)
            if item[1] > G:
                acc["published"] += item[1] - G
                G = item[1]
                publishes.append((t, G))
        changed = True
        while changed:
            changed = False
            if prod["state"] == "enqueue" and len(queue) < K:
                if publication == "release" and acc["generated"] > G:
                    acc["published"] += acc["generated"] - G
                    G = acc["generated"]
                    publishes.append((t, G))
                queue.append((prod["begin"], G))
                changed = True
                if t < stop:
                    prod.update(state="work", left=work, begin=t)
                    begins.append(t)
                else:
                    prod["state"] = "done"
            if cons["state"] == "wait" and queue:
                begin, token = queue.pop(0)
                if cons["blocked"]:
                    target = token if wake == "token" else G
                    if target > cons["local"]:
                        acc["skipped"] += target - cons["local"]
                        cons["local"] = target
                cons.update(state="settle", item=begin, next="work", blocked=False)
                changed = True
            if cons["state"] == "settle" and cons["local"] >= G:
                if cons["next"] == "work":
                    cons.update(state="work", left=c)
                else:
                    tx.append((cons["item"], t))
                    cons.update(state="wait", item=None)
                changed = True
        snapshots.append((t, len(begins), len(tx), G, acc["generated"] - acc["published"]))
        if prod["state"] == "done" and not queue and cons["state"] == "wait":
            break
        if cons["state"] == "wait":
            cons["blocked"] = True
        if prod["state"] == "work":
            prod["left"] -= 1
            prod["cpu"] += 1
            acc["producerWork"] += 1
            if credit and prod["cpu"] >= phase and (prod["cpu"] - phase) % T == 0:
                acc["samples"] += 1
                acc["generated"] += credit
                if publication == "immediate":
                    running = max([G] + [v for _, v in pending])
                    for part, offset in zip(parts, offsets):
                        running += part
                        pending.append((t + 1 + offset, running))
            if prod["left"] == 0:
                prod["state"] = "enqueue"
        if cons["state"] == "work":
            cons["left"] -= 1
            acc["consumerWork"] += 1
            if cons["left"] == 0:
                cons.update(state="settle", next="end")
        elif cons["state"] == "settle" and cons["local"] < G:
            cons["local"] += 1
            acc["paid"] += 1
        t += 1
        if t > stop + (K + 2) * (work + c) + acc["generated"] + max(offsets, default=0) + 1:
            raise RuntimeError("the model ran past the longest possible drain")
    for item in sorted(pending):
        if item[1] > G:
            acc["published"] += item[1] - G
            G = item[1]
            publishes.append((item[0], G))
    acc["finalDebt"] = G - cons["local"]
    acc["unpublished"] = acc["generated"] - acc["published"]
    if acc["generated"] != acc["samples"] * credit or acc["unpublished"] != 0:
        raise RuntimeError(f"credit is not conserved: {acc}")
    if acc["published"] != acc["skipped"] + acc["paid"] + acc["finalDebt"]:
        raise RuntimeError(f"delay is not accounted for: {acc}")
    return {"tx": tx, "publishes": publishes, "begins": begins, "stop": stop, "snapshots": snapshots, "accounting": acc, "G": G}


def g_at(publishes, time):
    value = 0
    for when, v in publishes:
        if when <= time:
            value = v
    return value


def counts_at(run, time):
    return sum(1 for b in run["begins"] if b <= time), sum(1 for _, e in run["tx"] if e <= time)


def upstream_at(run, warm, stop):
    """Pinned upstream counters for an experiment whose start and end snapshots are taken at warm and stop.

    arrivals = begins after the warm snapshot up to the stop snapshot, difference = absolute begins minus absolute ends at the stop,
    and duration = the ticks from warm to stop minus the delay published in them.
    """
    begun_warm, _ = counts_at(run, warm)
    begun, ended = counts_at(run, stop)
    duration = (stop - warm) - (g_at(run["publishes"], stop) - g_at(run["publishes"], warm))
    return begun - ended, begun - begun_warm, duration


def latency_or_none(difference, arrivals, duration):
    if arrivals <= 0 or duration <= 0:
        return None
    return difference / (arrivals / duration)


def estimators(run, warm, stops=()):
    """CozNet rules use the transactions that begin after the warm snapshot. Each upstream value states its admissible observations."""
    stop = run["stop"]
    if type(warm) is not int or not 0 <= warm < stop:
        raise ValueError(f"warm must be an integer from 0 to below the stop, not {warm!r}")
    pub = run["publishes"]
    tx = [(b, e) for b, e in run["tx"] if b > warm]
    signed = [(e - b) - (g_at(pub, e) - g_at(pub, b)) for b, e in tx]
    n = len(tx)
    out = {"n": n, "wall": sum(e - b for b, e in tx) / n if n else None, "clipped": sum(max(0, v) for v in signed) / n if n else None,
           "signed": sum(signed) / n if n else None, "negative": sum(v < 0 for v in signed)}
    difference, arrivals, duration = upstream_at(run, warm, stop)
    out["upstreamAtStop"] = latency_or_none(difference, arrivals, duration)
    occupancy = sum(min(e, stop) - max(b, warm) for b, e in run["tx"] if b < stop and e > warm) / (stop - warm)
    out["occupancySurrogate"] = latency_or_none(occupancy, arrivals, duration)
    if stops:
        samples = [upstream_at(run, warm, x) for x in stops]
        admissible = [x for x in samples if x[1] > 0 and x[2] > 0]
        out["trajectoryObservations"] = len(samples)
        out["excludedObservations"] = len(samples) - len(admissible)
        if admissible:
            out["trajectoryMean"] = sum(d / (a / u) for d, a, u in admissible) / len(admissible)
            total = sum(u for _, _, u in admissible)
            out["trajectoryViewerAggregate"] = (sum(d * u for d, _, u in admissible) / total) / (sum(a for _, a, _ in admissible) / total)
    return out


def gain(treatment, baseline, zero_allowed=True):
    """A gain is undefined without both latencies, with a baseline of 0 or less, or with a negative treatment.

    An upstream treatment of 0 is a valid observation, so it gives a gain of 1. The signed diagnostic passes zero_allowed=False,
    because its rule leaves a total of 0 or less undefined.
    """
    if treatment is None or baseline is None or baseline <= 0 or treatment < 0 or (treatment == 0 and not zero_allowed):
        return None
    return 1 - treatment / baseline
