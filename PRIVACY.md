# Privacy Policy for Zeroquery

**Effective date:** October 8, 2026

Zeroquery ("the app", "the software") is an open-source tool, available as a native Windows desktop application and as a self-hosted web/Docker stack, that turns a connected database into a natural-language queryable application. This policy explains what data the app processes, where it goes, and what it does not do.

## 1. Summary

- Zeroquery is **local-first**. The Desktop app runs entirely on your own machine; the web/Docker stack runs on infrastructure you control.
- We (the Zeroquery project maintainers) do **not** operate any backend servers, and we do **not** collect, receive, or have access to your database contents, connection strings, API keys, or queries.
- The app does **not** include any built-in analytics, telemetry, or crash-reporting SDKs.
- Data does leave your machine in one case: when you ask a natural-language question, relevant schema metadata, your query text, and the data needed to answer it are sent to the **LLM provider you configure** (e.g., OpenRouter, OpenAI, Groq, a self-hosted Ollama instance, or another OpenAI-compatible endpoint) so it can generate a response and decide which database tools to call.

## 2. Data processed by the app

### 2.1 Connection strings and LLM API keys
Stored only on your local machine (or your own server, for the Docker deployment), encrypted at rest using the Windows Data Protection API with AES-256 (`zqenc:v1:` format). These values are never transmitted to any Zeroquery-operated service, because no such service exists.

### 2.2 Database schema and query data
When you introspect a database, Zeroquery reads table/column metadata to let you select what to expose — this stays local unless you explicitly proceed to querying.

When you ask a question in natural language, the orchestration layer sends the following to your **configured LLM provider** as part of the tool-calling loop:
- Your question text and conversation history.
- Entity/schema descriptions for the tables you've enabled.
- Data returned by read/write tool calls (e.g., `read_records`) needed to answer your question — this can include actual rows from your database if your question requires them.

Because this data is sent to a third-party LLM provider of your choosing, it is subject to **that provider's own privacy policy and data-handling practices**, not this one. If you use a locally-hosted model (e.g., via Ollama), this data never leaves your machine.

### 2.3 Write/mutation audit log
Every database write you confirm (create/update/delete) is recorded to a local, tamper-evident audit log containing a timestamp, client IP, target entity, operation type, and before/after values. This log is stored locally and is not transmitted anywhere.

### 2.4 Network and security protections
The app applies IP-based rate limiting and SSRF defenses (blocking loopback/private-subnet/cloud-metadata targets for outbound connections you configure) purely as local safety mechanisms — these do not send data externally.

## 3. What we don't do

- We don't run servers that receive your data.
- We don't include analytics, telemetry, or crash-reporting SDKs in the app.
- We don't sell or share data, because we never receive it in the first place.

## 4. Third-party services

If you configure Zeroquery to use a hosted LLM provider (OpenRouter, OpenAI, Groq, etc.), your interactions with that provider are governed by its own terms and privacy policy. Review those directly with the provider you choose. Running a local model (e.g., Ollama) avoids sending data to any third party.

## 5. Children's privacy

Zeroquery is a developer tool and is not directed at, or intended for use by, children under 13.

## 6. Changes to this policy

This policy may be updated as the project evolves. Changes will be published in this file, in the same repository, with an updated effective date.

## 7. Contact

Zeroquery is maintained as an open-source project. For questions or concerns about this policy, please open an issue at [github.com/avikeid2007/ZeroQuery/issues](https://github.com/avikeid2007/ZeroQuery/issues).
