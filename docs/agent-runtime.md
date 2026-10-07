# Agent Runtime

Agents run statefully in memory and persist execution summaries to `agent-runs.json`. Agents may only invoke registered tools. Tool calls validate input/output schemas, use verified facts for authoritative candidate claims, and record hashes rather than raw sensitive payloads. Phase 2 supports only demo tools and demo LLM output. Any external action is rejected unless separately approved and allowed by the external action guard.
