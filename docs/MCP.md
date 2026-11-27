# Truckroll over MCP

Truckroll is an [MCP](https://modelcontextprotocol.io) server. An AI client such as Claude Desktop
can search customers, read job history, check the schedule, ask for dispatch suggestions and search
the knowledge base. It does all of that as one person: the person whose token it holds.

## Connecting Claude Desktop

1. Sign in to Truckroll and open **MCP access** in the dock (the key icon).
2. Create a token named after the device, for example "Work laptop". Truckroll shows the token
   **once**, together with a ready-made Claude Desktop config.
3. In Claude Desktop, open **Settings**, then **Developer**, then **Edit config**. Paste the
   `truckroll` entry under `mcpServers`, save, and restart Claude Desktop.

The config looks like this:

```json
{
  "mcpServers": {
    "truckroll": {
      "command": "npx",
      "args": ["-y", "mcp-remote", "http://localhost:3000/mcp", "--header", "Authorization:${TRUCKROLL_AUTH}"],
      "env": { "TRUCKROLL_AUTH": "Bearer trk_…" }
    }
  }
}
```

Claude Desktop starts local programs and talks to them over stdio. `mcp-remote` (it needs Node.js)
bridges that to Truckroll's HTTP endpoint. The token sits in an environment variable rather than
in the arguments, because a space inside an argument breaks on Windows.

Any client that speaks MCP's streamable HTTP transport can connect directly: `POST /mcp` with
`Authorization: Bearer trk_…`.

## The tools

| Tool | What it returns |
|---|---|
| `search_customers(query)` | Up to 10 customers matching a name, street or phone number, with their ids |
| `get_customer_history(customerId)` | The customer's homes, the equipment in each (model, serial, install date, warranty, manual code) and their last 20 jobs with technicians' notes |
| `list_jobs(day?, status?)` | Up to 50 jobs you can see. `day` is `today` (the default), `upcoming`, `past` or `all`. `status` is `open` or one status |
| `get_schedule(date?, branch?)` | A branch's dispatch board for one day: each technician's jobs, plus jobs still waiting for one |
| `suggest_technicians(jobId)` | The top three technicians for a job, ranked by [Truckroll's rules](adr/007-code-ranks-the-model-explains.md), with the facts behind each and who was ruled out. It suggests and never assigns |
| `ask_knowledge_base(question)` | Up to six numbered passages from manuals, policies, equipment and job history, found by [hybrid search](adr/003-retrieval-in-redis.md). The client's model writes the answer and cites them |
| `create_draft_note(jobId, text)` | Adds a note to a job, signed "Your Name (via MCP, draft)" and marked as an MCP note. This is the only change any tool can make |

Read-only tools are marked `readOnly` in their MCP annotations, so clients can tell them apart from
the one that writes.

## The security model

**A token is its owner, no more.**
- Every tool reads through `AccessScope`, the same layer that filters the web app, and most of them
  call the very endpoint methods the web app uses.
- A technician's token sees a technician's jobs, and a Guelph dispatcher's token sees Guelph.
- Tests run every tool as different people and check that each one gets only what its owner sees
  (`tests/Truckroll.Api.Tests/Mcp/McpTests.cs`).

**Contact details never leave.** Phone numbers and email addresses in tool results are replaced
with `[phone]` and `[email]`. Across Truckroll the rule is that contact details never reach a
model, and the client on the other end of MCP is a model. People still see the details in the web
app.

**Tokens and cookies don't mix.**
- `/mcp` accepts only personal tokens, so a signed-in browser can't drive it.
- The web API accepts only the browser cookie, so a leaked token can't open the CRM's own endpoints.
- Tokens can't create or revoke tokens.

**Tokens are stored as fingerprints.**
- A token is `trk_` followed by 32 random bytes, and is shown once.
- Truckroll stores only its SHA-256 hash, plus the first 8 characters so you can tell tokens apart.
- A plain fast hash is the right choice for this: unlike a password, a 256-bit random token can't
  be guessed, so there's nothing for a slow hash to protect.

**Revoking is immediate.**
- Every MCP request looks the token's hash up again, with no cache.
- A revoked or expired token, or one whose owner's account was removed, fails on its very next call.
- Tokens expire after 90 days.
- A person can hold at most 10 live tokens.

**Writes are drafts.** The one tool that changes anything adds a note that's labelled as written
through MCP. It can't book, assign, cancel, approve calls or touch customers.

**The server keeps no session.** Each request is authenticated and handled on its own, so there's
no session state to leak between users or to hijack.

## Not in v0.1

- **OAuth for third-party clients:** personal tokens are for a person's own client.
- **Audit log views:** a note written through MCP is marked as such, but there's no screen yet
  listing everything a token did.
