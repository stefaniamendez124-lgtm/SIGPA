import { createClient } from '@supabase/supabase-js';

const supabaseUrl = import.meta.env.VITE_SUPABASE_URL ?? 'https://your-project.supabase.co';
const supabaseKey = import.meta.env.VITE_SUPABASE_ANON_KEY ?? 'your-anon-key';

export const supabase = createClient(supabaseUrl, supabaseKey, {
  auth: {
    persistSession: true,
    autoRefreshToken: true,
    detectSessionInUrl: true,
  },
});

export const hasSupabaseConfig = Boolean(
  supabaseUrl && supabaseKey &&
  !supabaseUrl.includes('your-project') &&
  !supabaseKey.includes('your-anon-key'),
);