// src/App.tsx
import React, { useEffect, useState } from 'react';
import { RoleDashboard } from './components/dashboard/RoleDashboard';
import { hasSupabaseConfig, supabase } from './supabaseClient';

type UserSession = {
  id: string;
  name: string;
  email: string;
  role: 'Estudiante' | 'Profesor' | 'Coordinador' | 'Administrador';
  token?: string;
};

const normalizeRole = (value?: string): UserSession['role'] => {
  const normalized = (value ?? '').toLowerCase();

  if (normalized.includes('prof')) return 'Profesor';
  if (normalized.includes('coord')) return 'Coordinador';
  if (normalized.includes('admin')) return 'Administrador';

  return 'Estudiante';
};

export default function App() {
  const [session, setSession] = useState<UserSession | null>(null);
  const [loading, setLoading] = useState(true);
  const [email, setEmail] = useState('estudiante@sigpa.edu');
  const [password, setPassword] = useState('');
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [authLoading, setAuthLoading] = useState(false);

  useEffect(() => {
    let active = true;

    const restoreSession = async () => {
      if (!hasSupabaseConfig) {
        localStorage.removeItem('sigpa_session');
        setLoading(false);
        return;
      }

      try {
        const { data: sessionData, error: sessionError } = await supabase.auth.getSession();
        if (sessionError) {
          throw sessionError;
        }

        const authSession = sessionData.session;
        if (!authSession) {
          localStorage.removeItem('sigpa_session');
          return;
        }

        const { data: profile, error: profileError } = await supabase
          .from('usuarios')
          .select('nombre_completo, rol')
          .eq('id', authSession.user.id)
          .single();

        if (profileError || !profile) {
          await supabase.auth.signOut();
          localStorage.removeItem('sigpa_session');
          if (active) {
            setErrorMsg('No se encontró el perfil del usuario. Ejecuta el SQL de configuración de Supabase y vuelve a intentar.');
          }
          return;
        }

        if (active) {
          applySession({
            id: authSession.user.id,
            name: profile.nombre_completo || authSession.user.email || 'Usuario SIGPA',
            email: authSession.user.email ?? '',
            role: normalizeRole(profile.rol),
            token: authSession.access_token,
          });
        }
      } catch (restoreError) {
        if (active) {
          setErrorMsg(restoreError instanceof Error ? restoreError.message : 'No se pudo restaurar la sesión de Supabase.');
        }
      } finally {
        if (active) {
          setLoading(false);
        }
      }
    };

    void restoreSession();
    return () => {
      active = false;
    };
  }, []);

  const applySession = (user: UserSession) => {
    setSession(user);
    localStorage.setItem('sigpa_session', JSON.stringify(user));
  };

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    setAuthLoading(true);
    setErrorMsg(null);

    try {
      if (!hasSupabaseConfig) {
        setErrorMsg('Falta configurar la URL y la clave pública de Supabase en .env.');
        return;
      }

      const { data: authData, error: authError } = await supabase.auth.signInWithPassword({
        email,
        password,
      });

      if (authError || !authData.user || !authData.session) {
        setErrorMsg(authError?.message ?? 'No se pudo autenticar el usuario.');
        return;
      }

      const { data: profile, error: profileError } = await supabase
        .from('usuarios')
        .select('nombre_completo, rol')
        .eq('id', authData.user.id)
        .single();

      if (profileError || !profile) {
        await supabase.auth.signOut();
        setErrorMsg('No se encontró el perfil del usuario. Ejecuta el SQL de configuración de Supabase y vuelve a intentar.');
        return;
      }

      applySession({
        id: authData.user.id,
        name: profile.nombre_completo || authData.user.email || 'Usuario SIGPA',
        email: authData.user.email ?? email,
        role: normalizeRole(profile.rol),
        token: authData.session.access_token,
      });
    } catch (loginError) {
      setErrorMsg(loginError instanceof Error ? loginError.message : 'No se pudo conectar con Supabase.');
    } finally {
      setAuthLoading(false);
    }
  };

  const handleLogout = async () => {
    await supabase.auth.signOut();
    setSession(null);
    localStorage.removeItem('sigpa_session');
    setErrorMsg(null);
  };

  if (loading) {
    return (
      <div
        style={{
          minHeight: '100vh',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          background: '#0f172a',
          color: '#fff',
          fontFamily: 'sans-serif',
        }}
      >
        Cargando SIGPA...
      </div>
    );
  }

  if (!session) {
    return (
      <div
        style={{
          minHeight: '100vh',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          background: 'linear-gradient(135deg, #020817 0%, #0f172a 45%, #111827 100%)',
          padding: '24px',
          fontFamily: 'sans-serif',
        }}
      >
        <form
          onSubmit={handleLogin}
          style={{
            width: '100%',
            maxWidth: '420px',
            backgroundColor: '#111827',
            border: '1px solid rgba(148, 163, 184, 0.2)',
            borderRadius: '18px',
            boxShadow: '0 25px 60px rgba(15, 23, 42, 0.75)',
            padding: '32px 28px',
          }}
        >
          <div style={{ textAlign: 'center', marginBottom: '24px' }}>
            <div
              style={{
                margin: '0 auto 12px',
                width: '58px',
                height: '58px',
                borderRadius: '14px',
                background: 'linear-gradient(135deg, #2563eb, #7c3aed)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                fontWeight: 700,
                fontSize: '24px',
                color: '#fff',
              }}
            >
              S
            </div>
            <h2 style={{ margin: 0, color: '#f8fafc', fontSize: '30px' }}>SIGPA</h2>
            <p style={{ margin: '8px 0 0', color: '#94a3b8', fontSize: '14px' }}>
              Gestión de proyectos académicos
            </p>
          </div>

          {errorMsg && (
            <div
              style={{
                padding: '10px 12px',
                marginBottom: '18px',
                backgroundColor: '#7f1d1d',
                color: '#fecaca',
                borderRadius: '10px',
                fontSize: '14px',
                textAlign: 'center',
              }}
            >
              {errorMsg}
            </div>
          )}

          <div style={{ marginBottom: '18px' }}>
            <label style={{ display: 'block', color: '#cbd5e1', marginBottom: '8px', fontSize: '14px' }}>
              Correo electrónico
            </label>
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
              placeholder="estudiante@sigpa.edu"
              style={{
                width: '100%',
                boxSizing: 'border-box',
                padding: '12px 14px',
                borderRadius: '10px',
                border: '1px solid #334155',
                backgroundColor: '#0f172a',
                color: '#f8fafc',
                fontSize: '15px',
              }}
            />
          </div>

          <div style={{ marginBottom: '22px' }}>
            <label style={{ display: 'block', color: '#cbd5e1', marginBottom: '8px', fontSize: '14px' }}>
              Contraseña
            </label>
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
              placeholder="••••••••"
              style={{
                width: '100%',
                boxSizing: 'border-box',
                padding: '12px 14px',
                borderRadius: '10px',
                border: '1px solid #334155',
                backgroundColor: '#0f172a',
                color: '#f8fafc',
                fontSize: '15px',
              }}
            />
          </div>

          <div style={{ marginBottom: '14px', color: '#94a3b8', fontSize: '12px', lineHeight: 1.5 }}>
            Ingresa con una cuenta creada en Supabase Authentication.
          </div>

          <button
            type="submit"
            disabled={authLoading}
            style={{
              width: '100%',
              padding: '12px 16px',
              border: 'none',
              borderRadius: '10px',
              background: 'linear-gradient(135deg, #2563eb, #4f46e5)',
              color: '#fff',
              fontWeight: 700,
              fontSize: '16px',
              cursor: authLoading ? 'not-allowed' : 'pointer',
              opacity: authLoading ? 0.8 : 1,
            }}
          >
            {authLoading ? 'Verificando...' : 'Ingresar'}
          </button>
        </form>
      </div>
    );
  }

  return <RoleDashboard user={session} onLogout={handleLogout} />;
}