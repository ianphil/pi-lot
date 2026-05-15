# LlmSdk.Testing

`FauxLlmSdkClient` is an in-memory `ILlmSdkClient` for consumer tests. Script responses with `FauxResponse.Text`, `FauxResponse.ToolCall`, or `FauxResponse.Error`, pass them to the client constructor, then assert against `RecordedRequests` after calling `CompleteAsync` or `StreamAsync`.
