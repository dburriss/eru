# Plan: eru Architecture

For domain vocabulary and concept definitions see [domain-model.md](domain-model.md).

## Layers

Three projects; dependencies flow inward only.

```
┌─────────────────┐    ┌─────────────────┐
│    Eru.Cli      │    │  Eru.Adapters   │
│  CLI parsing    │    │  Concrete IO    │
│  Active patterns│    │  (git, fs, json)│
│  Program.fs     │    │                 │
└────────┬────────┘    └────────┬────────┘
         │                     │
         └──────────┬──────────┘
                    │ injects concrete fns
                    ▼
        ┌───────────────────────┐
        │      Eru.Domain       │
        │  see domain-model.md  │
        └───────────────────────┘
```

```
Eru.Cli        knows about: Eru.Domain, Eru.Adapters, Argu
Eru.Adapters   knows about: Eru.Domain types only
Eru.Domain     knows about: nothing outside itself
```

## Data flow

```
argv ──► Argu parser ──► active pattern ──► usecase ──► exit code
```

Active patterns map `ParseResults<EruArgs>` directly to usecase input types — no intermediate routing DU.

```fsharp
// Program.fs
match parsed with
| InitArgs cmd   -> Init.run deps cmd
| AddArgs cmd    -> Add.run deps cmd
| SearchArgs q   -> Search.run deps q
| SyncArgs opts  -> Sync.run deps opts
```

`Program.fs` wires deps and maps exit codes. No logic.

## Test references

```
Eru.Tests
 ├── Eru.Domain   → pure logic and usecases
 └── Eru.Adapters → IO integration (no CLI noise)
```
