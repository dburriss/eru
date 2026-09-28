module Eru.Tests.InboxWatchCliTests

open System
open Xunit
open Eru.Cli.InboxWatchCli

let private t (seconds: float) = DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero).AddSeconds(seconds)

[<Fact>]
let ``coalesceTriggers fires once for a single event, quietPeriodMs after it`` () =
    let triggers = coalesceTriggers 500 [ t 0.0 ]
    Assert.Equal<DateTimeOffset list>([ (t 0.0).AddMilliseconds 500.0 ], triggers)

[<Fact>]
let ``coalesceTriggers coalesces a burst within the quiet window into a single trigger after the last event`` () =
    // Three events 100ms apart, well within a 500ms quiet period — one trigger only.
    let triggers = coalesceTriggers 500 [ t 0.0; t 0.1; t 0.2 ]
    Assert.Equal<DateTimeOffset list>([ (t 0.2).AddMilliseconds 500.0 ], triggers)

[<Fact>]
let ``coalesceTriggers fires a separate trigger for events spaced further apart than the quiet period`` () =
    // Second event arrives after the first burst's trigger would already have fired.
    let triggers = coalesceTriggers 500 [ t 0.0; t 1.0 ]
    Assert.Equal<DateTimeOffset list>([ (t 0.0).AddMilliseconds 500.0; (t 1.0).AddMilliseconds 500.0 ], triggers)

[<Fact>]
let ``coalesceTriggers with no events produces no triggers`` () =
    Assert.Empty(coalesceTriggers 500 [])
