using System;

namespace Jellyfin.Plugin.Requests.Fulfilment;

/// <summary>
/// What one run of the fulfilment sweep did, kept so an operator can be told without reading a log.
/// <para>
/// A sweep that found nothing and a sweep that never ran look identical from the outside, and they
/// are the two answers an operator most needs separated when requests have stopped moving. The count
/// examined is here for the same reason the count moved is: a run that looked at nothing is a store
/// that answered with nothing, which is a different fault from a library that holds none of it.
/// </para>
/// </summary>
/// <param name="At">When the run finished.</param>
/// <param name="Examined">How many requests it looked at.</param>
/// <param name="Fulfilled">How many of them it moved to fulfilled.</param>
public readonly record struct SweepReport(DateTimeOffset At, int Examined, int Fulfilled);
