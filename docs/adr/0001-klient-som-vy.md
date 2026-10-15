# ADR 0001 – Unity-klient som vy, motoroberoende server, delad C#

**Status:** Beslutad 2026-10-16

## Beslut

- **Klient:** Unity 6 med URP.
- **Server:** en fristående .NET 10-konsolapp. Ingen Unity på servern.
- **Delad kod:** `Madlands.Shared` och `Madlands.ClientCore` (netstandard2.1, C# 9) kompileras både av dotnet och av Unity, som lokala UPM-paket.
- **Spärr mot motorkod:** asmdef-filerna har `noEngineReferences: true`, så delad kod kan aldrig använda UnityEngine.

## Varför

- Ett språk överallt.
- Servern kan testas och lasttestas headless, och botarna använder samma klientkod som spelet.
- Unity-klienten går att byta (licens, prestanda) utan att spellogiken behöver skrivas om.

Unreal valdes bort av tre skäl:

- Blueprints är binära.
- Dedikerade servrar kräver att motorn byggs från källkod.
- Arbetsflödet passar sämre för AI.

## Konsekvenser

Unitys C#-version begränsar Shared: inga records/init och inga file-scoped namespaces. Reglerna finns i `CLAUDE.md`.
