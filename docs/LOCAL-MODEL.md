# The local assistant model (Olmo 3 7B Instruct)

The Ask bar runs on a language model that lives on this computer. Nothing about a project is sent anywhere. O-CoreOp carries the model and O-Draft uses the same one.

## What is installed, and where

| Part | File | Where | Size |
|---|---|---|---|
| Model | `Olmo-3-7B-Instruct-Q4_K_M.gguf` (Allen AI, Apache-2.0, quantised by unsloth, revision `a2a9ae53…`) | `%LOCALAPPDATA%\HumanCentricWorks\models\` | 4 471 602 048 bytes, SHA-256 `0de0bc63…c5c4` |
| Runtime | llama.cpp `llama-server.exe`, release b11976, Vulkan build for Windows x64 (MIT) | `%LOCALAPPDATA%\HumanCentricWorks\runtime\llama-b11976-vulkan\` | 37 MB zip, SHA-256 `fd94feb5…8c10` |

Overrides: `OSAS_MODEL` names a model file, `OSAS_LLAMA_SERVER` names `llama-server.exe`. The app also looks in `models\` and `runtime\` beside its own exe. Weights are not in git. If either part is missing the Ask bar says which and where to put it; a stored Anthropic key is used only in that case.

## How it runs

`Office.Ai` (`BBSDesktop/Office.Ai`) starts `llama-server` as a hidden child process the first time anyone asks, on a free port of 127.0.0.1, behind a random key, 8192 tokens of context, one slot. A Windows job object ends it with the app, even if the app crashes. The GPU is used as far as its memory allows (the server fits layers on its own); the rest runs on the CPU. On this machine's RTX 4060 laptop GPU it answers at about 15 tokens a second and loads in about 7 seconds.

The in-process .NET binding (LLamaSharp 0.27/0.30) was tried first and dropped: 0.30 does not find its native library on .NET 8, and 0.27's bundled llama.cpp crashes creating the context for this model. Running the official server avoids both and moves with llama.cpp releases.

## How the assistant talks to it

`Office.Shared.OlmoChat` builds the Olmo 3 prompt exactly as the model's own template does: tool signatures in `<functions>` in the system turn, tool results in `environment` turns, and the model's calls read back from `<function_calls>name(arg=json, …)</function_calls>`. Text that came from a project or a tool result has its `<|im_` tokens broken up so it cannot forge a turn. A block that cannot be read gives no call. The tool set and its confirmation dialogs are unchanged: the model only proposes, the app asks the user before any change.

## How O-Draft uses it

While O-CoreOp is open it answers the suite's existing local contract on `127.0.0.1:8742` (`AiCoreListener`), the one O-Draft's `osas_ai` client already speaks:

- `GET /v1/health` gives `{"status":"ready","model_ready":true}` when the model and runtime are installed.
- `POST /v1/chat` takes `{messages:[{role,content}], max_tokens}` and returns `{"status":"ok","message":{"content":…}}`. The model loads on the first chat.
- Every call needs `Authorization: Bearer <token>`, the token in `%LOCALAPPDATA%\OSAS\ai\auth.token` (made if missing, or `OSAS_AI_TOKEN`).

O-Draft's own in-process llama.cpp (`aad-llm`, crate `llama-cpp-2` 0.1.159) cannot load this model: its vendored llama.cpp has no `olmo3` architecture. So O-Draft reaches the model through this contract, and its Ask bar works while O-CoreOp is running. Loading the file in O-Draft itself would need a newer `llama-cpp-2`.

## Not done

- The installer does not yet place the model and runtime; they were put in the folders above by hand.
- The CUDA build of llama.cpp (faster on NVIDIA cards, 280 MB) is not used.
- O-Draft's assistant still instructs the model in Qwen's `<tool_call>` JSON form; Olmo follows it in prose but is trained on `<function_calls>`, so tool calling from O-Draft is untested.
