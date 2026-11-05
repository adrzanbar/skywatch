# SkyWatch

Real-time aircraft pipeline for the K0sStreams / Kafka comparison lab.

The producer polls [adsb.lol](https://adsb.lol) (ODbL 1.0), turns each snapshot
into position events, and publishes them through a broker-neutral
`IMessageBus`. The bus adapter is selected by configuration:

```json
"Messaging": {
  "Mode": "Kafka"
}
```

Supported modes: `Kafka`, `K0sStreams`, `InMemory` (tests/smoke only). There is no
fanout and no broker-specific code in the producer or analytics.

## Services

| Service | Responsibility |
|---|---|
| `SkyWatch.Producer` | Polls adsb.lol zones, military and emergency squawks; publishes `flight-positions` |
| `SkyWatch.Tracker` | Stateful per-aircraft alert engine; publishes `flight-alerts` |
| `SkyWatch.Aggregator` | Airport congestion and global fleet snapshots; publishes `flight-stats` |

The dashboard is intentionally not part of this stage.

## Topics

- `flight-positions`
- `flight-alerts`
- `flight-stats`
- `flight-dlq`

Kafka manifests: `deploy/kafka/topics.yaml`
K0sStreams manifests: `deploy/k0sstreams/topics.yaml`

## Build and test

```bash
dotnet build SkyWatch.slnx
dotnet test SkyWatch.slnx
```

The live Kafka contract test is opt-in. Without `SKYWATCH_KAFKA_BOOTSTRAP` it returns
immediately; set it to exercise the real broker:

```bash
SKYWATCH_KAFKA_BOOTSTRAP=10.66.51.35:9094 \
  dotnet test tests/SkyWatch.Messaging.Tests/SkyWatch.Messaging.Tests.csproj
```

## Run with Kafka

```bash
Messaging__Mode=Kafka \
Messaging__Kafka__BootstrapServers=10.66.51.35:9094 \
dotnet run --project src/SkyWatch.Producer
```

```bash
Messaging__Mode=Kafka \
Messaging__Kafka__BootstrapServers=10.66.51.35:9094 \
dotnet run --project src/SkyWatch.Tracker
```

```bash
Messaging__Mode=Kafka \
Messaging__Kafka__BootstrapServers=10.66.51.35:9094 \
dotnet run --project src/SkyWatch.Aggregator
```

## Run with K0sStreams

K0sStreams only accepts writes on the leader. For a local run, port-forward to
the current leader rather than the service:

```bash
LEADER=$(kubectl get lease -n k0sstreams k0sstreams-leader -o jsonpath='{.spec.holderIdentity}')
kubectl port-forward -n k0sstreams "pod/$LEADER" 19090:9090
```

Then:

```bash
Messaging__Mode=K0sStreams \
Messaging__K0sStreams__BaseUrl=http://localhost:19090 \
dotnet run --project src/SkyWatch.Producer
```

Use the same `Messaging__Mode` and base URL for the tracker and aggregator.

## adsb.lol notes

- The public API is rate-limited; the source spaces requests (`Adsb:RequestSpacingMs`,
  default 500 ms) and retries a 429 once.
- A poll can return hundreds of aircraft. `Adsb:MaxPositionsPerSnapshot` (default 200)
  samples the snapshot evenly so K0sStreams, which publishes one HTTP request per
  record, stays within a poll interval.
- Default watch zones: Buenos Aires, Madrid, New York, London, Tokyo.
- Emergency squawks: 7500, 7600, 7700.
- `alt_baro: "ground"` is normalized to `OnGround = true`, altitude 0.
- Attribution: data from adsb.lol contributors, ODbL 1.0.
