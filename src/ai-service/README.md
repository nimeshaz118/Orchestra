AI Service – Python Setup

This folder contains the Python AI service for the Orchestra project.

The project uses **uv** to manage the Python version, virtual environment, and dependencies. Please use the commands in this guide so everyone works with the same dependency versions.

## 1. Prerequisites

Install the following:

- Git
- Python 3.13 (uv can install and manage this for you)
- uv

### Install uv

**Linux / macOS**

```bash
curl -LsSf https://astral.sh/uv/install.sh | sh
```

**Windows (PowerShell)**

```powershell
powershell -ExecutionPolicy ByPass -c "irm https://astral.sh/uv/install.ps1 | iex"
```

After installation, restart the terminal if the `uv` command is not recognised, then verify:

```bash
uv --version
```

Official uv installation guide: https://docs.astral.sh/uv/getting-started/installation/

## 2. Open the AI service directory

From the repository root:

```bash
cd src/ai-service
```

Run the remaining commands in this directory (the directory containing `pyproject.toml`).

## 3. Set up Python and install dependencies

Install Python 3.13 through uv:

```bash
uv python install 3.13
```

The repository's `.python-version` file specifies the Python version for this project. If that file does not exist on your branch, create it with:

```bash
uv python pin 3.13
```

Create the virtual environment and install the project dependencies:

```bash
uv sync
```

`uv sync` reads `pyproject.toml` and `uv.lock`, creates the local `.venv` environment if needed, and installs the locked dependencies.

Verify the environment:

```bash
uv run python --version
uv run python -c "import fastapi, langchain, langchain_core, langgraph, dotenv; print('Python dependencies installed successfully')"
```

## 4. Current Python dependencies

The current `pyproject.toml` defines these runtime dependencies:

| Package | Purpose |
|---|---|
| `fastapi` | Build the AI service HTTP API |
| `langchain` | Work with LLMs, prompts and integrations |
| `langchain-core` | Core LangChain abstractions and interfaces |
| `langgraph` | Build stateful, multi-step AI workflows and agents |
| `python-dotenv` | Load configuration from environment files |
| `uvicorn` | Run the private FastAPI HTTP service |

The version constraints are maintained in `pyproject.toml`. `uv.lock` records the resolved versions so team members can install a consistent environment.

## 5. Adding a dependency

Do not install project packages with `pip install`. Add dependencies using uv so that both `pyproject.toml` and `uv.lock` are updated.

For example:

```bash
uv add package-name
```

## 6. Running Python commands

You do not have to activate the virtual environment manually. Prefix Python commands with `uv run`:

```bash
uv run python your_script.py
```

## 7. Running the private AI service

Put the service token and provider keys in `src/ai-service/.env` for local development. Match `ORCHESTRA_AI_SERVICE_TOKEN` to the .NET API's `AiService:ServiceToken`. For example:

```dotenv
ORCHESTRA_AI_SERVICE_TOKEN=replace_with_the_shared_service_token
NVIDIA_API_KEY=replace_with_the_provider_key
NVIDIA_API_KEY_2=replace_with_the_second_provider_key
```

Register the public HTTPS base URL and the credential reference through the Super Admin API. The saved provider record supplies both values. Use a provider key name such as `NVIDIA_API_KEY_2` or `OPENROUTER_API_KEY`. Use `null` for an unauthenticated provider. FastAPI reloads local provider keys from `.env` on each test and can also read provider keys injected into its process environment. Keep internal service credentials separate from provider key variables. Keep the service on a private network when it runs on a separate host from the API.

Multiple provider records can use the same URL with separate keys. Each test uses the reference on the selected record.

```powershell
uv sync --locked
uv run --locked --env-file .env uvicorn main:app --host 127.0.0.1 --port 8000
```

Use the port configured by `AiService:BaseUrl` in the .NET API. Port `8000` matches the repository's development settings.

Run the diagnostics tests with `uv run --locked python -m unittest -v`.
