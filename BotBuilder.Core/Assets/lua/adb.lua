---@meta
--- ADB (A Damn Bot) — Lua scripting API.
--- These globals are injected by the "Run Lua Script" action at runtime. This file is for editor
--- autocomplete only; it is never executed. Note: the runtime is a hardened MoonSharp sandbox, so
--- os.execute / io / loadfile are NOT available — use the modules below instead.

--- Writes a message to the bot run log. Accepts any value; nil logs an empty line.
---@param message any
function log(message) end

--- Bot variables, bridged to and from the run. Seeded with the current run variables when the script
--- starts; values you set are written back to the run when the script finishes successfully. Setting a
--- key to nil unsets it. Values are scalars only: string, number, or boolean.
---@type table<string, string|number|boolean>
vars = {}

json = {}

--- Parses a JSON string into a Lua table.
---@param text string
---@return table
function json.parse(text) end

--- Encodes a Lua table as a JSON string.
---@param value table
---@return string
function json.encode(value) end

--- File system helpers. Operational failures (missing file, IO error) raise an error you can pcall or
--- let route to the action's onFailure port.
fs = {}

--- Reads the whole file at `path` as a string.
---@param path string
---@return string
function fs.read(path) end

--- Writes `content` to `path`, overwriting it.
---@param path string
---@param content string
function fs.write(path, content) end

--- Copies a file from `source` to `destination`.
---@param source string
---@param destination string
function fs.copy(source, destination) end

--- Moves a file from `source` to `destination`.
---@param source string
---@param destination string
function fs.move(source, destination) end

--- Returns true if a file exists at `path`.
---@param path string
---@return boolean
function fs.exists(path) end

--- Deletes the file at `path`.
---@param path string
function fs.delete(path) end

---@class ProcessResult
---@field exitCode integer  # the process exit code (non-zero is a value, not an error)
---@field stdout string     # captured standard output
---@field stderr string     # captured standard error

process = {}

--- Runs an external process to completion and returns its result. The executable is resolved via PATH.
--- A non-zero exit code is returned as a value; only a failure to START the process raises an error
--- (pcall-able / routes to onFailure).
---@param command string    # executable name or path, e.g. "adb"
---@param args? string[]    # argument list, e.g. { "shell", "getprop", "sys.boot_completed" }
---@return ProcessResult
function process.run(command, args) end

---@class HttpResponse
---@field status integer                 # HTTP status code (non-2xx is a value, not an error)
---@field body string                    # response body
---@field headers table<string, string>  # response headers

http = {}

--- Sends an HTTP GET request. A transport failure raises an error (pcall-able); a non-2xx status is a value.
---@param url string
---@param headers? table<string, string>  # optional request headers
---@return HttpResponse
function http.get(url, headers) end

--- Sends an HTTP POST request.
---@param url string
---@param body? string                    # optional request body
---@param headers? table<string, string>  # optional request headers
---@return HttpResponse
function http.post(url, body, headers) end

--- ============================================================================
--- NOT available in ADB's sandbox
--- ----------------------------------------------------------------------------
--- Scripts run in MoonSharp's soft sandbox, so several standard-library features
--- that exist in normal Lua are removed here (they evaluate to nil at runtime).
--- They are re-declared below as @deprecated so editors strike them through; the
--- generated .luarc.json additionally disables the `io` and `debug` libraries
--- outright. Use the ADB modules above instead:
---   io.*       -> fs                 os.execute -> process.run
---   os.remove  -> fs.delete          os.rename  -> fs.move
---   load / loadstring / loadfile / dofile / require -> (no dynamic code or modules)
--- STILL AVAILABLE from the standard library: os.time, os.date, os.clock,
--- os.difftime, plus the string / table / math / coroutine libraries, pcall, print.
--- ============================================================================

--- **Unavailable in ADB's sandbox.** Run external programs with `process.run` instead.
---@deprecated
---@return integer
function os.execute(command) end

--- **Unavailable in ADB's sandbox.**
---@deprecated
function os.exit(code) end

--- **Unavailable in ADB's sandbox.**
---@deprecated
---@return string?
function os.getenv(varname) end

--- **Unavailable in ADB's sandbox.** Delete files with `fs.delete` instead.
---@deprecated
function os.remove(filename) end

--- **Unavailable in ADB's sandbox.** Move files with `fs.move` instead.
---@deprecated
function os.rename(oldname, newname) end

--- **Unavailable in ADB's sandbox.**
---@deprecated
---@return string
function os.tmpname() end

--- **Unavailable in ADB's sandbox.**
---@deprecated
function os.setlocale(locale, category) end

--- **Unavailable in ADB's sandbox.** No dynamic code loading.
---@deprecated
function load(chunk, chunkname, mode, env) end

--- **Unavailable in ADB's sandbox.** No dynamic code loading.
---@deprecated
function loadstring(text, chunkname) end

--- **Unavailable in ADB's sandbox.** No file-based code loading; read with `fs.read`.
---@deprecated
function loadfile(filename, mode, env) end

--- **Unavailable in ADB's sandbox.** No file-based code loading.
---@deprecated
function dofile(filename) end

--- **Unavailable in ADB's sandbox.** No module system.
---@deprecated
function require(modname) end
