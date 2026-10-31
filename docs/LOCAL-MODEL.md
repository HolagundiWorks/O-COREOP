# The local assistant model (Olmo 3 7B Instruct)

The Ask bar runs on a language model that lives on this computer. Nothing about a project is sent anywhere. **O-Studio** hosts Olmo and serves it to O-CoreOp and O-Draft on loopback.

## What is installed, and where

| Part | File | Where | Size |
|---|---|---|---|
| Model | `Olmo-3-7B-Instruct-Q4_K_M.gguf` (Allen AI, Apache-2.0, quantised by unsloth, revision `a2a9ae53…`) | `%LOCALAPPDATA%\HumanCentricWorks\models\` | 4 471 602 048 bytes, SHA-256 `0de0bc63…c5c4` |
| Runtime | llama.cpp `llama-server.exe`, release b11976, Vulkan build for Windows x64 (MIT) | `%LOCALAPPDATA%\HumanCentricWorks\runtime\llama-b11976-vulkan\` | 37 MB zip, SHA-256 `fd94feb5…8c10` |

Overrides: `OSTUDIO_MODEL` names a model file, `OSTUDIO_LLAMA_SERVER` names `llama-server.exe`. The app also looks in `models\` and `runtime\` beside its own exe. Weights are not in git. If either part is missing the Ask bar says which and where to put it; a stored Anthropic key is used only in that case.

## How it runs

**Host:** When you sign in to O-Studio, it starts `AiCoreListener` on `127.0.0.1:8742` and loads the model on the first question (`SuiteAiCoordinator` in `Office.Ai`). Closing the workspace or O-Studio stops the listener and unloads the model.

**Clients:** O-CoreOp and O-Draft set `OSTUDIO_AI_ROLE=client` when launched from O-Studio. They call the host over HTTP (`RemoteLlm`, `POST /v1/completion` for O-CoreOp's tool loop, `POST /v1/chat` for O-Draft and O-Studio's assistant). O-CoreOp opened alone still hosts on `:8742` when the port is free (solo use).

`Office.Ai` starts `llama-server` as a hidden child process the first time the host answers, on a free port of 127.0.0.1, behind a random key, 16384 tokens of context, one slot. A Windows job object ends it with the host app. The GPU is used as far as its memory allows; the rest runs on the CPU.

The in-process .NET binding (LLamaSharp 0.27/0.30) was tried first and dropped: 0.30 does not find its native library on .NET 8, and 0.27's bundled llama.cpp crashes creating the context for this model. Running the official server avoids both and moves with llama.cpp releases.

## How the assistant talks to it

`Office.Shared.OlmoChat` builds the Olmo 3 prompt exactly as the model's own template does: tool signatures in `<functions>` in the system turn, tool results in `environment` turns, and the model's calls read back from `<function_calls>name(arg=json, …)</function_calls>`. Text that came from a project or a tool result has its `<|im_` tokens broken up so it cannot forge a turn. A block that cannot be read gives no call. The tool set and its confirmation dialogs are unchanged: the model only proposes, the app asks the user before any change.

## How O-Draft and O-CoreOp use it

While **O-Studio** is signed in (or O-CoreOp runs alone with the port free), the suite AI Core on `127.0.0.1:8742` (`AiCoreListener`) answers:

- `GET /v1/health` — `ready` when the model and runtime are installed.
- `POST /v1/chat` — `{messages:[{role,content}], max_tokens}` for O-Draft and O-Studio's assistant.
- `POST /v1/completion` — `{prompt, max_tokens}` for O-CoreOp's Olmo tool loop.
- `Authorization: Bearer <token>` — token in `%LOCALAPPDATA%\O-Studio\ai\auth.token` (or `OSTUDIO_AI_TOKEN`).

Apps launched from Studio get `OSTUDIO_AI_ROLE=client` so they never load weights in-process.

## Installer

`BBSDesktop/installer/build-installer.ps1` downloads the pinned llama.cpp release (SHA-256 checked, cached under `artifacts\cache`, or `LLAMA_RUNTIME_ZIP`) and bundles it in `runtime\` beside the app. The model is not in Setup (4.5 GB): the wizard has a task "Download the assistant model", on by default, that fetches it from the pinned Hugging Face revision with its SHA-256 checked and puts it in `%LOCALAPPDATA%\HumanCentricWorks\models`, where O-Draft also looks. An existing file is kept, a failed download does not stop the install (the Ask bar says where the file goes), and uninstalling leaves the model for O-Draft. Run for real on 2026-10-29: `build-installer.ps1` (inside the MSVC environment, with `-OfficeDllDir`) built a 73 MB Setup; a silent install with the model moved aside downloaded the 4 471 602 048 bytes, the hash matched, the app and the 53 runtime files were installed, and the installed O-CoreOp answered through the AI Core service. The installed app used the runtime in the shared folder, not the copy beside it (the beside-the-app copy was not exercised). Note: Windows PowerShell 5.1 read the build script wrongly without a BOM (an em dash became a quote), so it is saved with one.

## O-Draft's assistant

O-Draft's agent reads Olmo's `<tool_call>` JSON form (what its prompt asks for) and, since 2026-10-29, Olmo's own `<function_calls>name(arg=json)</function_calls>` form. Against O-Draft's real 16 KB tool prompt through this facade the model chose `audit_drawing`, `get_drawing_summary` and `purge_unused` correctly after the hint in `osas_ai.rs` was added (without it, "remove unused layers" was refused and a greeting triggered a tool call). The facade cuts a reply after the tool call, as the model otherwise invents the result. Run end to end on 2026-10-29 in O-Draft's window with this app serving the model: "remove unused layers and blocks" gave `get_drawing_summary`, then `purge_unused`, then the Allow/Decline bar; after Allow the model reported the 15 layers removed, and one `undo` ("Purge") restored all 18. Two things had to change for that: the model context went from 8K to 16K tokens (O-Draft's tool prompt alone is about 5K, so the second turn was refused as too long), and the hint now says to follow the schema exactly, never to ask permission in chat, and to call the tool after a result. Without the last sentence the model asked "shall I proceed?" instead of calling the tool.

## Not done

- The CUDA build of llama.cpp (faster on NVIDIA cards, 280 MB) is not used.
- O-Draft needs the host running (O-Studio signed in, or O-CoreOp solo). Its in-process llama.cpp cannot load Olmo 3.
