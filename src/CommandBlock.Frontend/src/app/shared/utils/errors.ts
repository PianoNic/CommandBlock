import { toast } from '@spartan-ng/brain/sonner';

/// A short, human message for a failed request. Understands the API's `{ error }` bodies and
/// ASP.NET ProblemDetails (`{ title, errors }`), and never surfaces raw HTML, stack traces or
/// Angular's "Http failure response for ..." text.
export function messageOf(err: unknown, fallback = 'Something went wrong.'): string {
  if (err && typeof err === 'object') {
    const status = 'status' in err ? Number((err as { status: unknown }).status) : NaN;
    const body = 'error' in err ? (err as { error: unknown }).error : undefined;

    if (body && typeof body === 'object') {
      const b = body as { error?: unknown; errors?: Record<string, unknown>; title?: unknown; detail?: unknown };
      if (typeof b.error === 'string' && b.error.trim()) return b.error;
      if (b.errors && typeof b.errors === 'object') {
        const first = Object.values(b.errors).flat()[0];
        if (typeof first === 'string' && first.trim()) return first;
      }
      if (typeof b.detail === 'string' && b.detail.trim()) return b.detail;
      if (typeof b.title === 'string' && b.title.trim() && status !== 500) return b.title;
    }
    // Plain-text bodies are fine when they're a sentence, not a stack trace or an HTML error page.
    if (typeof body === 'string' && body.trim() && body.length <= 200 && !body.trimStart().startsWith('<') && !body.includes('\n   at ')) {
      return body.trim();
    }

    if (status === 0) return "Can't reach CommandBlock. Check your connection and try again.";
    if (status === 401) return 'Your session has expired. Sign in again.';
    if (status === 403) return "You don't have permission to do that.";
    if (status === 404) return 'That no longer exists. Refresh the page.';
    if (status >= 500) return `${fallback} The server hit an unexpected error.`;
  }
  if (err instanceof Error && err.message) return err.message;
  return fallback;
}

/// Shows a failed request as an error toast.
export function toastError(err: unknown, fallback: string): void {
  toast.error(messageOf(err, fallback));
}
