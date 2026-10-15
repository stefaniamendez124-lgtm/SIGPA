import { supabase } from '../supabaseClient';

export async function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const apiBase = (import.meta.env.VITE_API_BASE || '').replace(/\/$/, '');
  if (!apiBase) {
    throw new Error('Configura VITE_API_BASE para usar las funciones del proyecto.');
  }

  const { data, error } = await supabase.auth.getSession();
  if (error || !data.session) {
    throw error ?? new Error('La sesión expiró. Inicia sesión nuevamente.');
  }

  const headers = new Headers(init.headers);
  headers.set('Authorization', `Bearer ${data.session.access_token}`);
  if (init.body && !(init.body instanceof FormData) && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }

  return fetch(`${apiBase}${path}`, { ...init, headers });
}

export async function getApiError(response: Response): Promise<string> {
  const body = await response.text();
  if (body) {
    try {
      const parsed: unknown = JSON.parse(body);
      if (typeof parsed === 'string') return parsed;
      if (typeof parsed === 'object' && parsed !== null) {
        const detail = 'detail' in parsed && typeof parsed.detail === 'string' ? parsed.detail : undefined;
        const title = 'title' in parsed && typeof parsed.title === 'string' ? parsed.title : undefined;
        const message = 'message' in parsed && typeof parsed.message === 'string' ? parsed.message : undefined;
        if (detail || title || message) return detail ?? title ?? message ?? body;
      }
    } catch {
      return body;
    }
  }
  return `La solicitud falló (HTTP ${response.status}).`;
}
