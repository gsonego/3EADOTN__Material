# Module 2 — Deck vs Manual cross-check

Reviewed 2026-08-31. Deck: 15 slides, dated 25 Aug. Manual: `manual/module02-manual.md`, rewritten 31 Aug.
Same pattern as Module 1: **the manual was revised into the Catalog storyline and the deck was not.**

---

## The short version

The deck teaches a **Todo console app**. The manual teaches the **Catalog API**. Every name, every entity and the entire second demo differ. Unlike Module 1, the deck's *concepts* are sound and its speaker notes are already good — the damage is concentrated in the demo slides and in one place where the deck argues against the manual.

| Question | Answer |
| --- | --- |
| Consistent? | **No.** Different app, database, container, partition key, storage account, blob container, resource group. |
| Aligned? | **Concepts yes, demos no.** Both cover Cosmos DB then Blob Storage in the right order. |
| Clear on what to teach? | Concepts yes. The two live demos are unteachable from the deck as written. |
| Improvable? | Yes — narrower job than Module 1. Roughly 4 slides rewritten, 3 added. |
| Slides better? | Yes — 15 slides, zero diagrams, and the two hierarchy slides are begging for one. |
| More theory/text? | A little. The deck is well-written; the gaps are the manual's new material. |
| Notes better? | Already decent on all 15 — this deck is ahead of Module 1. Needs additions, not rewrites. |
| More illustrations? | Yes — 6 candidates, and one of them is the most valuable diagram in the module. |

---

## 1. 🔴 The demo application is different, end to end

| | Deck | Manual |
| --- | --- | --- |
| Cosmos account | `cosmos-estiam-demo1` | `cosmos-estiam-dev-2` |
| Database / container | `TodoDb` / `Todos` | `CatalogDb` / `Titles` |
| Partition key | `/category` | `/genre` |
| Item shape | `{id, category, title, done}` | `{id, title, genre, year, description, posterUrl}` |
| Cosmos demo | **.NET Todo console app** (slide 9, 25 lines of C#) | **catalog-api-v3**, built and deployed as a Container App revision |
| Storage account | `stestiamdemo1` | `stestiamdev2` |
| Blob container | `demo-files` | `posters` (deliberately the real one the app uses) |
| Resource group | `rg-estiam-demo` | shared `$RG` = `rg-estiam-dev-2`, explicitly no new RG |

**Slide 9 is the worst of it.** A full slide of `CosmosClient` / `CreateItemAsync` / `ReplaceItemAsync` console code, for a demo the manual doesn't contain. The manual never opens a console app — it does `docker build` → `push` → `containerapp update` → move traffic. Slide 9 currently teaches a demo that no longer exists.

Same orphan-resource-group problem as Module 1: running the deck's slide 8 as printed creates resources outside the environment Modules 3-6 depend on.

## 2. 🔴 The deck argues against the manual on partition keys

This is the important one, and it is not a naming mismatch — it is a contradiction.

- **Deck slide 6** — good key = "many distinct values (e.g. category, customerId)"; bad key = "very few distinct values… that partition throttles even if others sit idle."
- **Manual §1.1** — deliberately chooses `/genre`, and says so plainly: *"with only ~6 genres, it's a low-cardinality key… a production catalog would likely partition by `id`."* It frames this as an honest trade-off and turns it into a discussion question.

So the live demo runs the deck's own textbook example of a **bad** partition key, while the slide on screen calls it bad. Handled well this is the best teaching moment in the module — a real design decision with a visible cost, not a tidy exam answer. Handled badly it just looks like the trainer contradicting himself.

The deck needs a slide that makes the trade-off explicit, sitting between slide 6 and the demo.

**Related, also missing:** manual §1.3 notes that because `genre` is the partition key and Cosmos cannot change a partition key in place, editing a title's genre in the UI is a *delete on the old partition plus a create on the new one, same id*. That is the concrete cost of the decision, made visible in the app. No slide.

**Slide 10's scenario check** (answer: `customerId`, ~2,000 distinct values) is still a good question, but landing it next to a 6-value demo key needs one bridging sentence, or students will notice the tension before you do.

## 3. 🔴 The Blob Storage payoff has no slides

Manual §2.3 — `catalog-api-v4`, `POST /titles/{id}/poster`, and the design decision that the app stores **only the blob name** on the Cosmos item and mints a **fresh short-lived SAS URL on every read**, because URLs expire.

That is section 2.2's "private by default, time-limited access" lesson moving from the CLI into application code — the entire point of the topic. It appears on no slide. (Exactly the shape of Module 1's missing revisions content.)

Also unrepresented: the deliberate **two-release discipline** (v3 Cosmos verified alone in class, *then* v4 Blob — not bundled), and §2.4's end-to-end verification through the UI.

## 4. 🟠 The pairing exercise — a decision, not a bug

Deck slide 14 and the agenda build Blob Storage around a **two-student A/B pairing exercise** (Student A uploads and sends a SAS URL, Student B opens it, then strips the token). Your notes record this as a deliberate scope decision on 2026-08-24.

The manual has no pairing exercise. §2.2 is a single trainer-led walkthrough. So one of these is out of date and it needs your call — with ~50 students and no TA, the pairing exercise is also the most logistically expensive thing in the module.

## 5. 🟡 Manual content with no slide or note

- **Cosmos connection string is a master key** — full read/write to the whole account, must be gitignored, and *"Module 3's Managed Identity removes the need for this secret entirely."* A deliberate set-up for Module 3, currently unspoken.
- **SAS clock skew** — a freshly generated `--as-user` SAS can 403 on the very first request (`skt` start time lands a second late). Wait a few seconds, retry the *same* URL. The manual says it "looks alarming the first time" — and it will, live, in front of the room.
- **Timing** — provider registration ~70s; Cosmos account creation "a few minutes, slower than most Module 1 resources". Two waits the presenter must fill.
- **The control/data-plane line is not drawn in the same place for every operation** — `container create` succeeded *before* the role was assigned; only `blob upload` failed. The deck's note flattens this to "data-plane operations need the role", losing the sharper point.

## 6. Deck quality (independent of the manual)

Better shape than Module 1's was. All 15 slides have real speaker notes, the tables on slides 7 and 13 are genuine tables, and the writing is clean. Issues:

- **Zero diagrams in 15 slides.** Slides 5 and 12 are hierarchy chains (`Account → Database → Container → Item`) assembled from text boxes and arrow glyphs — precisely the content that should be a picture.
- **Slide 9** is 25 lines of code for a dead demo (see §1).
- The agenda promises **4 items** but there is no divider for item 3; the pairing exercise sits inside Topic 2's flow.
- **2 hours is tight.** The manual's demos are now two full `docker build` → push → deploy → shift-traffic cycles plus Cosmos account creation. That is materially heavier than the console app it replaced, and nothing in the deck budgets it.

## 7. Illustrations worth adding

1. **Good key vs hot partition** — even spread across partitions vs one partition taking the traffic while others idle. The most valuable diagram in the module, and it makes §2's trade-off discussable.
2. **Account → Database → Container → Item** as a real nested diagram (replaces slide 5's text chain).
3. **Storage account → Container → Blob** (replaces slide 12's text chain).
4. **The SAS sequence** — plain URL → 409 → same URL + token → 200 → token stripped → fails again.
5. **The poster round-trip** — browser → API → blob stored by name only → fresh SAS minted per read. Makes §2.3's design decision visible.
6. **RU/s as a per-second budget** — request costs drawn against a budget line, with throttling past it.

## 8. Decisions needed before any editing

1. Rewrite the Cosmos demo slides around **catalog-api-v3**, and what to do with slide 9's console code — delete, or keep the SDK snippet as concept (the `CosmosClient` + CamelCase + `PartitionKey` mechanics are still real and exam-relevant, even though the app changed).
2. **Pairing exercise**: keep it, or follow the manual and make Blob Storage trainer-led?
3. How hard to lean into the **`/genre` trade-off** — a dedicated slide, or a speaker-note beat?
4. Same conventions as Module 1 (`$VAR` names + "examples only" footnote, native flat diagrams, Say/Show/Watch-for/Time notes)?

---

## 9. Status — what was changed (2026-08-31)

Rebuilt as **`Module2-Azure-Storage-Solutions-v2.pptx`**, 20 slides (was 15). Original untouched.

Decisions taken: pairing exercise dropped (Blob is now trainer-led, matching the manual); slide 9's Todo-console C# deleted and replaced with the real `catalog-api-v3` deploy; the `/genre` trade-off gets its own slide; four new native-shape diagrams.

| # | Slide | Change |
|---|---|---|
| 2 | Agenda | 4 items rewritten; item 3 is now the end-to-end check, not the pairing exercise |
| 5 | Cosmos hierarchy | **New diagram** — nested Account/Database/Container/Item, replaces the text-chain slide |
| 7 | Hot partition | **New diagram** — same traffic, two keys, one hitting 429 |
| 8 | `/genre` trade-off | **New slide** — what we chose vs what production would choose, plus the delete+create cost |
| 10 | Cosmos CLI demo | Rewritten: `$VAR` names, `CatalogDb`/`Titles`/`/genre`, shared `$RG`, master-key warning |
| 11 | catalog-api-v3 | **New demo slide** — build/push/update/shift-traffic, replaces the console app |
| 12 | Scenario check | Bridged to our own key: "then ask why OUR key breaks this rule" |
| 14 | Blob hierarchy | **New diagram**, replaces the text-chain slide |
| 16 | SAS | Rewritten as a trainer-led 4-step walkthrough; clock-skew gotcha added to notes |
| 17 | Poster round-trip | **New diagram** — name stored, SAS minted per read |
| 18 | catalog-api-v4 | **New demo slide** + the end-to-end definition of done |
| 19-20 | Wrap-up, Next | Takeaways updated; new `Next: Module 3` closer |

Notes rewritten on every slide (Say / Show / Watch for / Time), including the timing waits (~70s provider registration, minutes for the Cosmos account) and the SAS clock-skew 403. Verified by rendering all 20 pages: no missing backgrounds, no empty notes, no overflow.

**No manual changes needed** — unlike Module 1, no factual errors were found in `module02-manual.md`.

Not done: the SAS-sequence diagram (not selected), and an RU/s budget visual.
