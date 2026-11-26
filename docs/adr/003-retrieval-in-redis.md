# ADR-003: Keep the search index in Redis 8, with MySQL as the source of truth

- **Status:** Accepted
- **Date:** 2026-11-26

## Context

The assistant answers from passages: manual sections, equipment records, jobs and their notes.
Finding the right passage takes three kinds of search at once, because the questions people ask
mix three kinds of evidence:

- **Meaning:** "the furnace lights then cuts out" should find a section titled "Burners light
  then shut off". That's vector search.
- **Words:** "flame sensor microamps" should rank the passage that says exactly that. That's
  keyword search (BM25).
- **Identifiers:** "J-24051", "CAL80-19-10234", "SB-2024-07". Embeddings blur these into their
  neighbours, and keyword tokenisers split them at the hyphens. In showroom-ai, the earlier
  project, identifiers were out-voted by fuzzier matches until they got their own exact ranking.

The records live in MySQL 8.4. MySQL Community has no vector distance search (HeatWave's vector
store is Oracle Cloud only), and its full-text search can't be combined with one in a single
query. The stack already runs Redis 8 for caching, and since Redis 8.0 the open-source image ships
the Query Engine: vector (HNSW), full-text and tag fields in one index.

## Decision

- One Redis index, `passages`, holds every passage with a `text` and a `title` field (BM25), an
  `ids` tag field (exact identifiers), filter tags (`source`, `record`, `branch`, `customer`) and
  a 768-dimension HNSW vector with cosine distance.
- MySQL stays the source of truth. The index is derived and disposable: it can be dropped and
  rebuilt from MySQL at any time (`POST /api/dev/reindex`, about 40 seconds for the demo company).
- Text is cut along markdown headings for documents, one passage per unit, and one per job with
  its notes. Phone numbers and emails are masked before anything is embedded or stored.
- A background worker re-indexes a job seconds after it changes, through the same job events the
  dispatch board uses. At startup it rebuilds the index if it's missing or was built with a
  different schema version, embedding model or vector width.
- The index doesn't decide who may see a passage. Retrieval (#12) confirms every hit against the
  asker's `AccessScope` in MySQL, because a technician's visibility changes with every assignment
  and an index can't be trusted to keep up.

## Consequences

- One query can run all three rankings against one copy of the data, and the fusion (#12) happens
  in C# where it can be tested and tuned.
- There's a second store to keep in step. The worker, the startup check and the rebuild command
  are the price, and a short delay between saving a note and finding it is accepted.
- Writes per record are transactional, so a search never sees a job half-replaced. A full rebuild
  isn't: searches during a rebuild see a partial index. That's acceptable for a demo; production
  would build into a second index and switch an alias.
- Aspire starts Redis with its own command line, which skips the image's module loading. The
  AppHost asks for the Query Engine explicitly with `WithModule(RedisModules.Search)`.

## Alternatives considered

- **pgvector or a dedicated vector database.** Another database to run, back up and secure, for
  one feature. Redis is already in the stack.
- **MySQL HeatWave.** Vector search exists only in Oracle's cloud service, not in MySQL Community.
- **Vector search alone.** Simpler, and the measured weak spot: exact identifiers and rare words.
- **Indexing in MySQL full-text plus vectors in Redis.** Two queries against two stores per
  question, and no way to filter both by the same tags.
