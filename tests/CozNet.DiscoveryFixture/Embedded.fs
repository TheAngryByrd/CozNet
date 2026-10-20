module CozNet.DiscoveryFixture.Embedded

open System.Runtime.CompilerServices

type Marker = class end

[<MethodImpl(MethodImplOptions.NoInlining)>]
let anchored (value: int) =
    value + 1
