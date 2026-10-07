# LLM Provider

`DemoLlmProvider` is the default and requires no network or API key. `OpenAiLlmProvider` is optional and disabled unless configured with a secret recovered from Windows-protected storage. LLM providers receive only validated inputs. They never receive OAuth tokens and cannot directly invoke high-risk actions.
