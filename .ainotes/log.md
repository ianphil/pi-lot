# AI Notes — Log

## 2026-04-04
- auth: Current Linux Copilot CLI metadata can store `last_logged_in_user` as a `{ host, login }` object and `logged_in_users` as an array of objects, so the metadata reader must handle more than simple string/map shapes.
- validation: Linux auth smoke checks need both `/health` and `/v1/models` or `llm models`, because healthy auth state alone only proves a token was loaded, not that the upstream credential works.
