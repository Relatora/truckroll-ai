# Demo script

A two-minute walkthrough, for a video or a live call. It follows the recorded demo
([truckroll-demo.mp4](demo/truckroll-demo.mp4)) and adds the two parts a recording can't show
well: a second board moving live, and a call becoming a job. Timings are a guide.

**Before you start:**

- The stack is running, and Ollama has answered one warm-up question, so the model is loaded.
- Press **Reset demo data** on the `api` resource in the Aspire dashboard, so today's board is fresh.
- Use light mode, a browser at 1440×900, and a second window signed in as Alex, side by side.

---

### 0:00 The front door (10 s)

Open http://localhost:3000.

> "Truckroll is a CRM for a heating, cooling and plumbing company with five branches. It's
> built on ASP.NET Core 10, EF Core, MySQL, Redis, SignalR and Next.js, and everything runs on
> this one machine, including the language model."

Click **Sign in**.

### 0:10 Who you are decides what you see (15 s)

Pick **Alex Romero, Dispatcher, Guelph**, and sign in. You land on the dispatch board.

> "Alex dispatches for Guelph, so Alex sees Guelph. One layer in the API decides that, and
> everything goes through it: the pages, the assistant's search and the MCP tools."

### 0:25 The board, live (25 s)

Drag the orange **14 Maple St** emergency from Waiting onto **Ben Okoro's** lane. It refuses,
and the lane says why.

> "Ben has no gas licence, and this is a furnace. That rule is in code, not in anyone's head."

Drag it onto **Nadia Kowalczyk's** lane. It lands, and the second window moves with it.

> "Every open board moves at once, over SignalR."

### 0:50 Who should go, and why (20 s)

Click the job, then **Open the job**. Point at **Who should go**.

> "The ranking is plain C#: licence first, then skill, timing, distance and workload. Every
> number on this card comes from the code. The model only writes the sentence underneath, and
> it can't change the order. A dispatcher still makes the call."

### 1:10 Ask Truckroll (25 s)

Open **Ask Truckroll** and ask:

> _What did we install at 14 Maple St?_

While it streams:

> "This searches three ways at once: by meaning, by words, and by exact identifiers like
> serial and model numbers. Then it keeps only what Alex is allowed to open. The sources show
> before the answer starts."

Click citation **1**. The equipment record opens beside the answer.

> "Every claim has a source you can open. If the sources don't cover it, it says so."

### 1:35 A phone call becomes a job (20 s)

Sign in as **Dana Whitfield** (CSR) and open **Calls**. Pick Priya Raman's call, then
**Analyse the call**.

> "The model reads the call and drafts a job and a follow-up email. Code matches the customer
> and works out the date. Nothing is booked until Dana approves it, and Truckroll never sends
> email: the follow-up goes to an outbox for a person to send."

Click **Approve**. The job is booked, and the draft is in the Outbox.

### 1:55 Close (5 s)

> "And all of it is open to Claude Desktop through MCP, as the person who made the token, with
> the same rules."

---

## If something goes wrong live

- **A slow first answer:** the model is loading. Keep talking; the sources show first.
- **The board says "Reconnecting":** the API restarted. Refresh the page.
- **The data looks wrong for today:** **Reset demo data**, then wait about a minute for the
  search index to rebuild before you ask a question.
