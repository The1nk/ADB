#!/bin/bash
# Prompt-aware SummonAIKit skill guidance and production contract verification.

if [ "${SUMMONAIKIT_INTERNAL_GENERATION:-}" = "1" ]; then
  exit 0
fi

INPUT="$(cat)"
TEXT="$(printf '%s' "$INPUT" | tr '[:upper:]' '[:lower:]')"
HOOK_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$HOOK_DIR/../.." && pwd)"
CONTRACT_PATH="$HOOK_DIR/.summonaikit-task-contract.txt"
LIKELY_SKILLS=""
RISK_SIGNALS=""
EXPERIENCE_SIGNALS=""
PROVIDER_DOCS=""
EXPLICIT_OVERRIDE=""
PREFERRED_SERVICES=""

add_skill() {
  case " $LIKELY_SKILLS " in
    *" $1 "*) ;;
    *) LIKELY_SKILLS="$LIKELY_SKILLS $1" ;;
  esac
}

add_signal() {
  case " $RISK_SIGNALS " in
    *" $1 "*) ;;
    *) RISK_SIGNALS="$RISK_SIGNALS
- $1" ;;
  esac
}

add_experience_signal() {
  case " $EXPERIENCE_SIGNALS " in
    *" $1 "*) ;;
    *) EXPERIENCE_SIGNALS="$EXPERIENCE_SIGNALS
- $1" ;;
  esac
}

add_provider_docs() {
  case " $PROVIDER_DOCS " in
    *" $1 "*) ;;
    *) PROVIDER_DOCS="$PROVIDER_DOCS
- $1" ;;
  esac
}

add_preferred_service() {
  case " $PREFERRED_SERVICES " in
    *" $1 "*) ;;
    *) PREFERRED_SERVICES="$PREFERRED_SERVICES
- $1" ;;
  esac
}

run_contract_check() {
  cd "$PROJECT_ROOT" || exit 0
  [ -f "$CONTRACT_PATH" ] || exit 0
  CONTRACT="$(cat "$CONTRACT_PATH")"
  HAS_PROD_CONTRACT=""
  HAS_UI_CONTRACT=""
  HAS_RATE_LIMIT_CONTRACT=""
  HAS_WEBHOOK_CONTRACT=""
  HAS_DB_CONCURRENCY_CONTRACT=""
  if printf '%s' "$CONTRACT" | grep -Eiq '^risk:|abuse/rate-limit guard|database/concurrency|webhook/side-effect flow|email/external side effect|API/auth flow'; then HAS_PROD_CONTRACT="1"; fi
  if printf '%s' "$CONTRACT" | grep -Eiq 'abuse/rate-limit guard|rate-limit-abuse'; then HAS_RATE_LIMIT_CONTRACT="1"; fi
  if printf '%s' "$CONTRACT" | grep -Eiq 'webhook/side-effect flow|webhook-side-effect'; then HAS_WEBHOOK_CONTRACT="1"; fi
  if printf '%s' "$CONTRACT" | grep -Eiq 'database/concurrency|database-concurrency'; then HAS_DB_CONCURRENCY_CONTRACT="1"; fi
  if printf '%s' "$CONTRACT" | grep -Eiq 'experience:|UI/interface change|form/flow UX|state coverage|accessibility|responsive layout'; then HAS_UI_CONTRACT="1"; fi
  [ -n "$HAS_PROD_CONTRACT$HAS_UI_CONTRACT" ] || exit 0

  # Build the diff from staged + unstaged + untracked, EXCLUDING the hook's own
  # generated files (the contract file is rewritten every prompt and the hook
  # script carries every keyword). Checks then evaluate only the user's real code
  # — and brand-new files that were never git-added are still scanned.
  HOOK_EXCLUDE=":(exclude).claude/hooks/*"
  UNTRACKED="$(git ls-files --others --exclude-standard -- . 2>/dev/null | grep -v '^.claude/hooks/')"
  DIFF_FILES="$( { git diff --name-only --diff-filter=ACMRT -- . "$HOOK_EXCLUDE" 2>/dev/null; git diff --cached --name-only --diff-filter=ACMRT -- . "$HOOK_EXCLUDE" 2>/dev/null; echo "$UNTRACKED"; } | sed '/^$/d' | sort -u)"
  [ -n "$DIFF_FILES" ] || exit 0
  DIFF="$( { git diff -- . "$HOOK_EXCLUDE" 2>/dev/null; git diff --cached -- . "$HOOK_EXCLUDE" 2>/dev/null; } )"
  if [ -n "$UNTRACKED" ]; then
    UNTRACKED_DIFF="$(echo "$UNTRACKED" | while IFS= read -r f; do [ -n "$f" ] && [ -f "$f" ] && sed 's/^/+/' "$f"; done)"
    DIFF="$DIFF
$UNTRACKED_DIFF"
  fi
  [ -n "$DIFF" ] || exit 0

  FAILED=""
  if [ -n "$HAS_RATE_LIMIT_CONTRACT" ] && printf '%s' "$CONTRACT" | grep -Eiq 'Cloudflare Workers' && ! printf '%s' "$CONTRACT" | grep -Eiq '^override:1'; then
    # Require evidence of an ACTUAL native limiter, not just the substring
    # "ratelimit" in an identifier/comment (a DB counter named ...RateLimit would
    # satisfy that). Accept: case-sensitive Alchemy resource decl  RateLimit({ ... },
    # an unstable_RateLimit binding, a wrangler rate_limits entry, or the runtime
    # call shape  .limit({ key: ... }).
    if ! { printf '%s' "$DIFF" | grep -Eq 'RateLimit[[:space:]]*\([[:space:]]*\{|unstable_RateLimit|rate_limits?[[:space:]]*[:=]' || printf '%s' "$DIFF" | grep -Eiq '\.limit[[:space:]]*\([[:space:]]*\{[^}]*key'; }; then
      FAILED="$FAILED
- Native provider limiter missing: this repo appears to use Cloudflare Workers; use the provider RateLimit binding through infra/runtime config, or state an explicit user/project override."
    fi
  fi

  if [ -n "$HAS_WEBHOOK_CONTRACT" ] && printf '%s' "$DIFF" | grep -Eiq 'webhook|validateEvent|WebhookVerificationError|POLAR_WEBHOOK_SECRET|order\.created|order\.paid'; then
    if ! printf '%s' "$DIFF" | grep -Eiq 'validateEvent|WebhookVerificationError|constructEvent|verifyWebhook|standardwebhooks|svix|x[_-]?signature|signature|401|fail[ -]?closed'; then
      FAILED="$FAILED
- Webhook signature check missing: verify the provider signature before side effects and fail closed when the secret is absent."
    fi
    if printf '%s' "$DIFF" | grep -Eiq 'delete[[:space:]]*\([^)]*(polarWebhookEvent|webhook).*|compensating delete|claim.*delete'; then
      FAILED="$FAILED
- Retry/idempotency warning: avoid fallible compensating deletes for claimed webhook events; prefer transaction rollback or explicit pending/processed status."
    fi
    if printf '%s' "$DIFF" | grep -Eiq 'purchasedAt' &&
       printf '%s' "$DIFF" | grep -Eiq '(select|find|query).{0,240}purchasedAt.{0,240}(update|set)' &&
       ! printf '%s' "$DIFF" | grep -Eiq 'isNull|IS NULL|where.{0,120}purchasedAt|returning'; then
      FAILED="$FAILED
- Business idempotency warning: payment conversion gates should use an atomic conditional write (for example WHERE purchased_at IS NULL RETURNING), not read-then-update."
    fi
  fi

  if [ -n "$HAS_DB_CONCURRENCY_CONTRACT" ] && printf '%s' "$DIFF" | grep -Eiq 'organization|membership|member' &&
     printf '%s' "$DIFF" | grep -Eiq '(insert|create).{0,120}(organization|membership|member)' &&
     ! printf '%s' "$DIFF" | grep -Eiq 'unique index|unique constraint|onConflict|on conflict|do nothing|upsert'; then
    FAILED="$FAILED
- Relational uniqueness warning: membership/link creation needs a DB uniqueness invariant plus idempotent insert/upsert behavior."
  fi

  # globalThis / frontend-only shared state is always an anti-pattern here. The
  # softer signals (new Map / setTimeout / process.env / fire-and-forget) are
  # common in legitimate code, so they fire only when they co-occur with
  # shared-state/limiter context AND the diff has no tracked background work or
  # native primitive (waitUntil / executionCtx / Durable Object / RateLimit) that
  # makes them correct — e.g. a "fire-and-forget" comment next to
  # c.executionCtx.waitUntil(...) is the RIGHT pattern, not an anti-pattern.
  if [ -n "$HAS_PROD_CONTRACT" ] && { printf '%s' "$DIFF" | grep -Eiq 'globalThis|frontend-only|client-side only' || { printf '%s' "$DIFF" | grep -Eiq 'new[[:space:]]+Map|setTimeout[[:space:]]*\(|process\.env|fire-and-forget|fire and forget' && printf '%s' "$DIFF" | grep -Eiq 'rate|limit|throttl|counter|cache|session|shared[ -]?state|idempoten|webhook|lock|queue|in[ -]?memory' && ! printf '%s' "$DIFF" | grep -Eq 'waitUntil|executionCtx|Durable Object|RateLimit[[:space:]]*\('; }; }; then
    FAILED="$FAILED
- Serverless anti-pattern detected in the diff: avoid module/global counters, frontend-only guards, detached timers, and untyped process.env runtime access."
  fi

  if [ -n "$HAS_PROD_CONTRACT" ] && printf '%s' "$DIFF" | grep -Eiq '(select|find|count).{0,220}(insert|update|increment|count[[:space:]]*\+).{0,220}(rate|limit|throttl|counter|otp|abuse)' &&
     ! printf '%s' "$DIFF" | grep -Eiq 'on conflict|onConflict|unique constraint|unique index|for update|serializable|transaction|returning|RateLimit[[:space:]]*\(|\.limit[[:space:]]*\('; then
    FAILED="$FAILED
- Non-atomic fallback counter detected: DB limiters must use an atomic statement, transaction/lock, unique-window upsert, or provider-native primitive."
  fi

  if [ -n "$HAS_UI_CONTRACT" ]; then
    UI_FILES="$(printf '%s
' "$DIFF_FILES" | grep -E '\.(tsx|jsx|css|scss|sass|mdx)$|(^|/)components/|(^|/)app/|(^|/)pages/' || true)"
    if [ -n "$UI_FILES" ]; then
      UI_DIFF="$(printf '%s
' "$UI_FILES" | xargs git diff -- 2>/dev/null)"
      if printf '%s' "$UI_DIFF" | grep -Eiq 'onSubmit|mutate\(|useMutation|fetch\(|form' &&
         ! printf '%s' "$UI_DIFF" | grep -Eiq 'loading|pending|disabled|isSubmitting|error|retry|toast|aria-|label'; then
        FAILED="$FAILED
- UI/UX state coverage warning: changed form or async UI without obvious loading/disabled/error/recovery handling."
      fi
      if printf '%s' "$UI_DIFF" | grep -Eiq '<(button|input|select|textarea)|role=' &&
         ! printf '%s' "$UI_DIFF" | grep -Eiq 'aria-|label|focus-visible|disabled|type='; then
        FAILED="$FAILED
- Accessibility warning: changed interactive UI without obvious labels, semantics, focus, or disabled handling."
      fi
      if printf '%s' "$UI_DIFF" | grep -Eiq 'table|rows?|columns?|thead|tbody|accordion|faq|disclosure|dialog|modal|tabs?' &&
         ! printf '%s' "$UI_DIFF" | grep -Eiq '<table|<thead|<tbody|<ul|<ol|<dl|<dialog|role=|aria-expanded|aria-controls|Table|Accordion|Dialog|Tabs'; then
        FAILED="$FAILED
- Semantic structure warning: changed table/list/disclosure/dialog-like UI without an obvious native element, ARIA state, or project accessibility primitive."
      fi
      if printf '%s' "$UI_DIFF" | grep -Eiq 'className=' &&
         ! printf '%s' "$UI_DIFF" | grep -Eiq 'sm:|md:|lg:|xl:|max-w|min-w|truncate|overflow-|flex-wrap|grid-cols'; then
        FAILED="$FAILED
- Responsive/layout warning: changed styled UI without obvious responsive constraints, truncation, or overflow handling."
      fi
    fi
  fi

  if [ -n "$FAILED" ]; then
    cat <<CHECK_EOF
SUMMONAIKIT CONTRACT CHECK WARNING
$FAILED

Review before finishing if these findings apply to your current changes. This Stop hook is advisory and will not block completion because repository-wide diffs may include unrelated pre-existing work.
CHECK_EOF
    exit 0
  fi

  exit 0
}

if [ "$SUMMONAIKIT_HOOK_PHASE" = "verify" ]; then
  run_contract_check
fi

if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])csharp([^[:alnum:]_]|$)'; then add_skill 'csharp'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])dotnet([^[:alnum:]_]|$)'; then add_skill 'dotnet'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])wpf([^[:alnum:]_]|$)'; then add_skill 'wpf'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])frontend-design([^[:alnum:]_]|$)|(^|[^[:alnum:]_])frontend[[:space:]]+design([^[:alnum:]_]|$)'; then add_skill 'frontend-design'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])ux([^[:alnum:]_]|$)'; then add_skill 'ux'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])adb-client([^[:alnum:]_]|$)|(^|[^[:alnum:]_])adb[[:space:]]+client([^[:alnum:]_]|$)'; then add_skill 'adb-client'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])playwright([^[:alnum:]_]|$)'; then add_skill 'playwright'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])opencvsharp([^[:alnum:]_]|$)'; then add_skill 'opencvsharp'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])moonsharp([^[:alnum:]_]|$)'; then add_skill 'moonsharp'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])tesseract([^[:alnum:]_]|$)'; then add_skill 'tesseract'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])xunit([^[:alnum:]_]|$)'; then add_skill 'xunit'; fi

if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])rate[[:space:]]+limit([^[:alnum:]_]|$)|(^|[^[:alnum:]_])ratelimit([^[:alnum:]_]|$)|(^|[^[:alnum:]_])too[[:space:]]+many([^[:alnum:]_]|$)|(^|[^[:alnum:]_])throttle([^[:alnum:]_]|$)|(^|[^[:alnum:]_])repeated[[:space:]]+request([^[:alnum:]_]|$)|(^|[^[:alnum:]_])spam([^[:alnum:]_]|$)|(^|[^[:alnum:]_])abuse([^[:alnum:]_]|$)|(^|[^[:alnum:]_])brute[[:space:]]+force([^[:alnum:]_]|$)|(^|[^[:alnum:]_])bruteforce([^[:alnum:]_]|$)|(^|[^[:alnum:]_])otp([^[:alnum:]_]|$)|(^|[^[:alnum:]_])recovery([^[:alnum:]_]|$)'; then add_signal 'abuse/rate-limit guard'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])background([^[:alnum:]_]|$)|(^|[^[:alnum:]_])after[[:space:]]+response([^[:alnum:]_]|$)|(^|[^[:alnum:]_])fire[[:space:]]+and[[:space:]]+forget([^[:alnum:]_]|$)|(^|[^[:alnum:]_])fire-and-forget([^[:alnum:]_]|$)|(^|[^[:alnum:]_])async[[:space:]]+cleanup([^[:alnum:]_]|$)|(^|[^[:alnum:]_])defer([^[:alnum:]_]|$)|(^|[^[:alnum:]_])later([^[:alnum:]_]|$)'; then add_signal 'background/lifecycle work'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])cron([^[:alnum:]_]|$)|(^|[^[:alnum:]_])scheduled([^[:alnum:]_]|$)|(^|[^[:alnum:]_])schedule([^[:alnum:]_]|$)|(^|[^[:alnum:]_])nightly([^[:alnum:]_]|$)|(^|[^[:alnum:]_])daily([^[:alnum:]_]|$)|(^|[^[:alnum:]_])hourly([^[:alnum:]_]|$)|(^|[^[:alnum:]_])recurring([^[:alnum:]_]|$)|(^|[^[:alnum:]_])interval([^[:alnum:]_]|$)'; then add_signal 'scheduled/recurring work'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])cache([^[:alnum:]_]|$)|(^|[^[:alnum:]_])shared[[:space:]]+state([^[:alnum:]_]|$)|(^|[^[:alnum:]_])counter([^[:alnum:]_]|$)|(^|[^[:alnum:]_])lock([^[:alnum:]_]|$)|(^|[^[:alnum:]_])queue([^[:alnum:]_]|$)|(^|[^[:alnum:]_])mutex([^[:alnum:]_]|$)|(^|[^[:alnum:]_])global([^[:alnum:]_]|$)|(^|[^[:alnum:]_])in[[:space:]]+memory([^[:alnum:]_]|$)|(^|[^[:alnum:]_])in-memory([^[:alnum:]_]|$)'; then add_signal 'cache/shared state'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])secret([^[:alnum:]_]|$)|(^|[^[:alnum:]_])env([^[:alnum:]_]|$)|(^|[^[:alnum:]_])environment[[:space:]]+variable([^[:alnum:]_]|$)|(^|[^[:alnum:]_])credential([^[:alnum:]_]|$)|(^|[^[:alnum:]_])token([^[:alnum:]_]|$)|(^|[^[:alnum:]_])api[[:space:]]+key([^[:alnum:]_]|$)|(^|[^[:alnum:]_])binding([^[:alnum:]_]|$)'; then add_signal 'secrets/env wiring'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])database([^[:alnum:]_]|$)|(^|[^[:alnum:]_])postgres([^[:alnum:]_]|$)|(^|[^[:alnum:]_])transaction([^[:alnum:]_]|$)|(^|[^[:alnum:]_])concurrent([^[:alnum:]_]|$)|(^|[^[:alnum:]_])race([^[:alnum:]_]|$)|(^|[^[:alnum:]_])atomic([^[:alnum:]_]|$)|(^|[^[:alnum:]_])idempotent([^[:alnum:]_]|$)|(^|[^[:alnum:]_])idempotency([^[:alnum:]_]|$)|(^|[^[:alnum:]_])unique([^[:alnum:]_]|$)|(^|[^[:alnum:]_])constraint([^[:alnum:]_]|$)|(^|[^[:alnum:]_])membership([^[:alnum:]_]|$)|(^|[^[:alnum:]_])member([^[:alnum:]_]|$)|(^|[^[:alnum:]_])organization([^[:alnum:]_]|$)|(^|[^[:alnum:]_])upsert([^[:alnum:]_]|$)'; then add_signal 'database/concurrency'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])webhook([^[:alnum:]_]|$)|(^|[^[:alnum:]_])callback([^[:alnum:]_]|$)|(^|[^[:alnum:]_])event([^[:alnum:]_]|$)|(^|[^[:alnum:]_])retry([^[:alnum:]_]|$)|(^|[^[:alnum:]_])signature([^[:alnum:]_]|$)|(^|[^[:alnum:]_])payment([^[:alnum:]_]|$)|(^|[^[:alnum:]_])checkout([^[:alnum:]_]|$)|(^|[^[:alnum:]_])subscription([^[:alnum:]_]|$)|(^|[^[:alnum:]_])order\.created([^[:alnum:]_]|$)|(^|[^[:alnum:]_])order\.paid([^[:alnum:]_]|$)|(^|[^[:alnum:]_])duplicate([^[:alnum:]_]|$)|(^|[^[:alnum:]_])idempotent([^[:alnum:]_]|$)|(^|[^[:alnum:]_])idempotency([^[:alnum:]_]|$)'; then add_signal 'webhook/side-effect flow'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])email([^[:alnum:]_]|$)|(^|[^[:alnum:]_])send([^[:alnum:]_]|$)|(^|[^[:alnum:]_])resend([^[:alnum:]_]|$)|(^|[^[:alnum:]_])polar([^[:alnum:]_]|$)|(^|[^[:alnum:]_])analytics([^[:alnum:]_]|$)|(^|[^[:alnum:]_])posthog([^[:alnum:]_]|$)|(^|[^[:alnum:]_])external[[:space:]]+api([^[:alnum:]_]|$)|(^|[^[:alnum:]_])side[[:space:]]+effect([^[:alnum:]_]|$)'; then add_signal 'email/external side effect'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])api([^[:alnum:]_]|$)|(^|[^[:alnum:]_])endpoint([^[:alnum:]_]|$)|(^|[^[:alnum:]_])route([^[:alnum:]_]|$)|(^|[^[:alnum:]_])handler([^[:alnum:]_]|$)|(^|[^[:alnum:]_])procedure([^[:alnum:]_]|$)|(^|[^[:alnum:]_])auth([^[:alnum:]_]|$)|(^|[^[:alnum:]_])session([^[:alnum:]_]|$)|(^|[^[:alnum:]_])invite([^[:alnum:]_]|$)|(^|[^[:alnum:]_])checkout([^[:alnum:]_]|$)|(^|[^[:alnum:]_])recovery([^[:alnum:]_]|$)'; then add_signal 'API/auth flow'; fi

if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])ui([^[:alnum:]_]|$)|(^|[^[:alnum:]_])interface([^[:alnum:]_]|$)|(^|[^[:alnum:]_])component([^[:alnum:]_]|$)|(^|[^[:alnum:]_])page([^[:alnum:]_]|$)|(^|[^[:alnum:]_])screen([^[:alnum:]_]|$)|(^|[^[:alnum:]_])layout([^[:alnum:]_]|$)|(^|[^[:alnum:]_])style([^[:alnum:]_]|$)|(^|[^[:alnum:]_])styling([^[:alnum:]_]|$)|(^|[^[:alnum:]_])design([^[:alnum:]_]|$)|(^|[^[:alnum:]_])visual([^[:alnum:]_]|$)|(^|[^[:alnum:]_])dashboard([^[:alnum:]_]|$)|(^|[^[:alnum:]_])landing([^[:alnum:]_]|$)|(^|[^[:alnum:]_])modal([^[:alnum:]_]|$)|(^|[^[:alnum:]_])dialog([^[:alnum:]_]|$)|(^|[^[:alnum:]_])sidebar([^[:alnum:]_]|$)|(^|[^[:alnum:]_])hero([^[:alnum:]_]|$)|(^|[^[:alnum:]_])card([^[:alnum:]_]|$)|(^|[^[:alnum:]_])table([^[:alnum:]_]|$)'; then add_experience_signal 'UI/interface change: Inspect nearby components/screens before inventing a new visual language. Match the target surface: dashboard/tooling should be dense and scannable; marketing can be more memorable; CLI should stay stable and readable. Avoid generic AI-looking UI unless the project already uses that style intentionally.'; add_skill 'frontend-design'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])form([^[:alnum:]_]|$)|(^|[^[:alnum:]_])field([^[:alnum:]_]|$)|(^|[^[:alnum:]_])input([^[:alnum:]_]|$)|(^|[^[:alnum:]_])submit([^[:alnum:]_]|$)|(^|[^[:alnum:]_])validation([^[:alnum:]_]|$)|(^|[^[:alnum:]_])settings([^[:alnum:]_]|$)|(^|[^[:alnum:]_])profile([^[:alnum:]_]|$)|(^|[^[:alnum:]_])account([^[:alnum:]_]|$)|(^|[^[:alnum:]_])change[[:space:]]+email([^[:alnum:]_]|$)|(^|[^[:alnum:]_])change[[:space:]]+password([^[:alnum:]_]|$)|(^|[^[:alnum:]_])login([^[:alnum:]_]|$)|(^|[^[:alnum:]_])sign[[:space:]]+in([^[:alnum:]_]|$)|(^|[^[:alnum:]_])signup([^[:alnum:]_]|$)|(^|[^[:alnum:]_])checkout([^[:alnum:]_]|$)|(^|[^[:alnum:]_])billing([^[:alnum:]_]|$)|(^|[^[:alnum:]_])wizard([^[:alnum:]_]|$)|(^|[^[:alnum:]_])flow([^[:alnum:]_]|$)'; then add_experience_signal 'form/flow UX: Map the user intent, preconditions, success state, failure state, and recovery path before coding. Cover loading, disabled, pending, success, error, and retry states in the UI. Keep validation server-backed; client validation is for guidance and speed, not trust.'; add_skill 'ux'; add_skill 'frontend-design'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])empty([^[:alnum:]_]|$)|(^|[^[:alnum:]_])loading([^[:alnum:]_]|$)|(^|[^[:alnum:]_])skeleton([^[:alnum:]_]|$)|(^|[^[:alnum:]_])error([^[:alnum:]_]|$)|(^|[^[:alnum:]_])failed([^[:alnum:]_]|$)|(^|[^[:alnum:]_])retry([^[:alnum:]_]|$)|(^|[^[:alnum:]_])disabled([^[:alnum:]_]|$)|(^|[^[:alnum:]_])pending([^[:alnum:]_]|$)|(^|[^[:alnum:]_])expired([^[:alnum:]_]|$)|(^|[^[:alnum:]_])success([^[:alnum:]_]|$)'; then add_experience_signal 'state coverage: Define the state matrix before editing component structure. Make failure and recovery understandable without leaking sensitive backend details. Ensure dynamic state text fits its container across supported viewports.'; add_skill 'ux'; add_skill 'frontend-design'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])accessibility([^[:alnum:]_]|$)|(^|[^[:alnum:]_])a11y([^[:alnum:]_]|$)|(^|[^[:alnum:]_])keyboard([^[:alnum:]_]|$)|(^|[^[:alnum:]_])focus([^[:alnum:]_]|$)|(^|[^[:alnum:]_])aria([^[:alnum:]_]|$)|(^|[^[:alnum:]_])screen[[:space:]]+reader([^[:alnum:]_]|$)|(^|[^[:alnum:]_])contrast([^[:alnum:]_]|$)|(^|[^[:alnum:]_])label([^[:alnum:]_]|$)|(^|[^[:alnum:]_])tab[[:space:]]+order([^[:alnum:]_]|$)'; then add_experience_signal 'accessibility/interaction quality: Use native semantics or accessible primitives before custom controls. Verify labels, focus states, keyboard paths, contrast, and disabled behavior. Do not hide required guidance behind hover-only UI.'; add_skill 'ux'; add_skill 'frontend-design'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])responsive([^[:alnum:]_]|$)|(^|[^[:alnum:]_])mobile([^[:alnum:]_]|$)|(^|[^[:alnum:]_])desktop([^[:alnum:]_]|$)|(^|[^[:alnum:]_])tablet([^[:alnum:]_]|$)|(^|[^[:alnum:]_])breakpoint([^[:alnum:]_]|$)|(^|[^[:alnum:]_])overflow([^[:alnum:]_]|$)|(^|[^[:alnum:]_])truncate([^[:alnum:]_]|$)|(^|[^[:alnum:]_])data[[:space:]]+dense([^[:alnum:]_]|$)|(^|[^[:alnum:]_])table([^[:alnum:]_]|$)'; then add_experience_signal 'responsive layout: Set stable layout constraints before adding dynamic content. Check mobile, desktop, long text, and dense data cases. Prefer predictable navigation and scan paths for operational/product surfaces.'; add_skill 'ux'; add_skill 'frontend-design'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])conversion([^[:alnum:]_]|$)|(^|[^[:alnum:]_])onboarding([^[:alnum:]_]|$)|(^|[^[:alnum:]_])activation([^[:alnum:]_]|$)|(^|[^[:alnum:]_])upgrade([^[:alnum:]_]|$)|(^|[^[:alnum:]_])paywall([^[:alnum:]_]|$)|(^|[^[:alnum:]_])pricing([^[:alnum:]_]|$)|(^|[^[:alnum:]_])cta([^[:alnum:]_]|$)|(^|[^[:alnum:]_])funnel([^[:alnum:]_]|$)|(^|[^[:alnum:]_])adoption([^[:alnum:]_]|$)'; then add_experience_signal 'conversion/onboarding flow: Clarify the next action and the reason to continue. Keep measurement and UI state aligned with the existing product analytics pattern. Do not replace in-app UX with broad marketing copy unless the task is explicitly a marketing surface.'; add_skill 'ux'; add_skill 'frontend-design'; fi

if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])cloudflare([^[:alnum:]_]|$)|(^|[^[:alnum:]_])worker([^[:alnum:]_]|$)|(^|[^[:alnum:]_])wrangler([^[:alnum:]_]|$)|(^|[^[:alnum:]_])alchemy\.run\.ts([^[:alnum:]_]|$)|(^|[^[:alnum:]_])@cloudflare/workers-types([^[:alnum:]_]|$)'; then add_provider_docs 'Cloudflare Workers: Search Cloudflare docs for the task capability before choosing a fallback. Service catalog: https://developers.cloudflare.com/products/ Runtime docs: https://developers.cloudflare.com/workers/ Best practices: https://developers.cloudflare.com/workers/platform/best-practices/'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])vercel([^[:alnum:]_]|$)|(^|[^[:alnum:]_])vercel\.json([^[:alnum:]_]|$)|(^|[^[:alnum:]_])@vercel/([^[:alnum:]_]|$)'; then add_provider_docs 'Vercel: Search Vercel docs for the task capability and runtime recommendation before choosing a fallback. Service catalog: https://vercel.com/docs Runtime docs: https://vercel.com/docs/functions Best practices: https://vercel.com/docs/frameworks'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])polar([^[:alnum:]_]|$)|(^|[^[:alnum:]_])@polar-sh([^[:alnum:]_]|$)|(^|[^[:alnum:]_])polar_([^[:alnum:]_]|$)'; then add_provider_docs 'Polar: Search Polar docs and the SDK webhook helpers before handling payment, subscription, or checkout events. Service catalog: https://polar.sh/docs Runtime docs: https://polar.sh/docs/integrate/webhooks/endpoints Best practices: https://polar.sh/docs/integrate/webhooks/delivery'; fi
if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])alchemy([^[:alnum:]_]|$)|(^|[^[:alnum:]_])alchemy\.run\.ts([^[:alnum:]_]|$)'; then add_provider_docs 'Alchemy IaC: Search Alchemy provider/resource docs for how this repo declares runtime services and bindings. Service catalog: https://alchemy.run/ Runtime docs: https://alchemy.run/'; fi

if [ -n "$RISK_SIGNALS" ]; then
  if [ -f "packages/infra/alchemy.run.ts" ] || [ -f "alchemy.run.ts" ] || find . -maxdepth 4 \( -name 'wrangler.toml' -o -name 'wrangler.json' -o -name 'wrangler.jsonc' \) -print -quit 2>/dev/null | grep -q . || grep -R "Cloudflare Workers\|@cloudflare/workers-types" package.json packages apps 2>/dev/null | head -n 1 | grep -q .; then
    add_skill 'cloudflare-workers'
    add_provider_docs 'Cloudflare Workers: Search Cloudflare docs for the task capability before choosing a fallback. Service catalog: https://developers.cloudflare.com/products/ Runtime docs: https://developers.cloudflare.com/workers/ Best practices: https://developers.cloudflare.com/workers/platform/best-practices/ Rate limiting docs: https://developers.cloudflare.com/workers/runtime-apis/bindings/rate-limit/'
  fi
  if [ -f "packages/infra/alchemy.run.ts" ] || [ -f "alchemy.run.ts" ] || grep -R '"alchemy"' package.json packages apps 2>/dev/null | head -n 1 | grep -q .; then
    add_skill 'alchemy'
    add_provider_docs 'Alchemy IaC: Search Alchemy provider/resource docs for how this repo declares runtime services and bindings. Service catalog: https://alchemy.run/ Runtime docs: https://alchemy.run/'
  fi
fi

if printf '%s' "$TEXT" | grep -Eiq '(^|[^[:alnum:]_])do[[:space:]]+not[[:space:]]+use([^[:alnum:]_]|$)|(^|[^[:alnum:]_])don[[:space:]]+t[[:space:]]+use([^[:alnum:]_]|$)|(^|[^[:alnum:]_])without([^[:alnum:]_]|$)|(^|[^[:alnum:]_])avoid([^[:alnum:]_]|$)|(^|[^[:alnum:]_])must[[:space:]]+use([^[:alnum:]_]|$)|(^|[^[:alnum:]_])use[[:space:]]+postgres([^[:alnum:]_]|$)|(^|[^[:alnum:]_])use[[:space:]]+redis([^[:alnum:]_]|$)|(^|[^[:alnum:]_])use[[:space:]]+database([^[:alnum:]_]|$)|(^|[^[:alnum:]_])do[[:space:]]+not[[:space:]]+touch[[:space:]]+infra([^[:alnum:]_]|$)|(^|[^[:alnum:]_])no[[:space:]]+cloudflare([^[:alnum:]_]|$)|(^|[^[:alnum:]_])no[[:space:]]+bindings([^[:alnum:]_]|$)'; then EXPLICIT_OVERRIDE="1"; fi

if printf '%s' "$RISK_SIGNALS" | grep -Eiq 'abuse/rate-limit guard' && printf '%s' "$PROVIDER_DOCS" | grep -Eiq 'Cloudflare Workers'; then
  add_preferred_service 'Cloudflare Workers Rate limiting: prefer RateLimit binding through infrastructure/runtime config; docs https://developers.cloudflare.com/workers/runtime-apis/bindings/rate-limit/; avoid Map/request-local counters and DB fallback unless explicitly overridden.'
fi

if [ -z "$LIKELY_SKILLS" ] && [ -z "$RISK_SIGNALS" ] && [ -z "$EXPERIENCE_SIGNALS" ] && [ -z "$PROVIDER_DOCS" ]; then
  exit 0
fi

if [ -n "$EXPLICIT_OVERRIDE" ]; then
  OVERRIDE_NOTE="
Explicit user override detected: follow the user preference. Still mention the provider-recommended path if relevant, and make the chosen fallback safe for concurrency, side effects, and failure modes."
fi

if [ -n "$RISK_SIGNALS$EXPERIENCE_SIGNALS" ]; then
  mkdir -p "$HOOK_DIR" 2>/dev/null
  {
    printf 'risk:%s\n' "$RISK_SIGNALS"
    printf 'experience:%s\n' "$EXPERIENCE_SIGNALS"
    printf 'override:%s\n' "$EXPLICIT_OVERRIDE"
    printf 'skills:%s\n' "$LIKELY_SKILLS"
    printf 'providers:%s\n' "$PROVIDER_DOCS"
    printf 'preferred:%s\n' "$PREFERRED_SERVICES"
  } > "$CONTRACT_PATH" 2>/dev/null
fi

cat <<EOF
SUMMONAIKIT SKILL PREFLIGHT

Likely skills to inspect/load:$LIKELY_SKILLS

Production risk scan:$RISK_SIGNALS

UI/UX quality scan:$EXPERIENCE_SIGNALS

Provider docs/service discovery:$PROVIDER_DOCS
$OVERRIDE_NOTE

CAPABILITY TASK CONTRACT

Likely skills:$LIKELY_SKILLS
Preferred native services:$PREFERRED_SERVICES

SKILL ADVANTAGE CONTRACT

Required behavior:
- Use skills to outperform the unskilled baseline, not just to add constraints.
- Inspect the nearest real repo patterns before inventing structure: routes/pages, components, tests, schema, infra, copy, analytics, and existing UX flows.
- Identify the highest-leverage win for this task, such as user-visible correctness, integration quality, accessibility, security, reliability, maintainability, operability, or speed of future change.
- Reuse existing product primitives before reimplementing: components, hooks, helpers, data registries, metadata builders, analytics, pricing, checkout, auth, and routing utilities.
- Use semantic, accessible structures for core content and controls: tables for tabular comparisons, lists for lists, forms for forms, buttons for actions, and project accessibility primitives for complex UI.
- Centralize repeated facts, labels, claims, and product defaults in shared registries or helpers when multiple surfaces need the same answer.
- Synthesize the best parts of available approaches when the evidence shows a hybrid is stronger.
- Check product claims against implemented behavior; do not imply automation, integrations, refresh behavior, security, metrics, counts, cadences, or data flow that the code does not provide.
- Ship the complete slice when relevant: registry entry, route/page, metadata, responsive behavior, analytics, tests, docs, migration, or infra wiring.

CLARIFYING QUESTIONS CONTRACT

Required behavior:
- Using skills must not reduce the assistant's willingness to ask questions.
- If the user's request is underspecified, ambiguous, or has product/UX/security tradeoffs that cannot be resolved from local context, ask the smallest useful set of clarifying questions before coding.
- If a reasonable assumption is safe, state it briefly and proceed; if the assumption could change architecture, user-visible behavior, data shape, or external side effects, ask first.

UI/UX QUALITY CONTRACT

Required checks:
- Inspect nearby screens/components before inventing new structure, styling, or copy.
- Reuse existing components and hooks for repeated UI jobs such as tables, FAQs/accordions, forms, sticky CTAs, pricing, checkout, navigation, and analytics-triggered controls.
- Identify the state matrix for changed interactive surfaces: loading, empty, error, disabled, pending, success, and recovery.
- Preserve the project's component library, design tokens, motion style, density, and interaction patterns.
- Avoid generic AI slop, template-looking layouts, ungrounded gradient/card stacks, and one-size-fits-all UI.
- Verify responsive behavior, accessibility basics, semantic structures, long text, focus/keyboard paths, ARIA state for disclosure widgets, and failure copy before finishing.

Required checks:
- Inspect provider service catalog/docs and project runtime config before selecting a fallback.
- Wire platform capabilities through infrastructure/runtime config, typed env/context, and the handler/procedure boundary.
- Guard before external payment APIs, auth mutations, email sends, analytics events, storage writes, and database mutations.
- Fallbacks must be durable, multi-instance safe, and atomic under concurrency.
- The final hook check will flag missing native primitives, non-atomic fallback counters, and serverless anti-patterns.

Before coding:
- Load or explicitly inspect the likely skills that apply to this task.
- Preserve normal collaboration behavior: ask clarifying questions when the request is ambiguous, underspecified, or depends on user preference that cannot be inferred from the repo.
- Use the skills as a quality multiplier: inspect local patterns, reuse existing primitives, identify the task's highest-leverage review criteria, and combine the strongest repo-consistent pieces instead of merely satisfying the narrow prompt.
- If any production risk appears, inspect the provider service catalog, best-practice docs, runtime/database config, and project docs before choosing an implementation.
- If any UI/UX signal appears, inspect nearby screens/components, identify states, reuse component-library primitives, preserve design system conventions, and avoid generic AI-looking UI.
- If the user or project docs clearly require a different mechanism, follow that requirement and call out the tradeoff.
- Otherwise prefer the platform-recommended or durable primitive before in-memory, frontend-only, detached async, or ad hoc counter solutions.
- Place guards before external side effects and document failure behavior.
EOF
