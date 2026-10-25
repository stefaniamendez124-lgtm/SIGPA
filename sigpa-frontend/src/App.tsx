// src/App.tsx
import React, { useEffect, useState } from 'react';
import { RoleDashboard } from './components/dashboard/RoleDashboard';
import { hasSupabaseConfig, supabase } from './supabaseClient';
import { apiFetch, getApiError } from './utility/api';

type UserSession = {
  id: string;
  name: string;
  email: string;
  role: 'Estudiante' | 'Profesor' | 'Coordinador' | 'Administrador';
  token?: string;
  mustChangePassword?: boolean;
};

type UserProfile = {
  nombre_completo: string | null;
  rol: string;
  activo: boolean;
  debe_cambiar_contrasena: boolean;
};

async function loadUserProfile(userId: string): Promise<{
  profile: UserProfile | null;
  error: Error | null;
}> {
  const currentResult = await supabase
    .from('usuarios')
    .select('nombre_completo, rol, activo, debe_cambiar_contrasena')
    .eq('id', userId)
    .maybeSingle();

  if (!currentResult.error) {
    return {
      profile: currentResult.data
        ? {
            nombre_completo: currentResult.data.nombre_completo,
            rol: currentResult.data.rol,
            activo: currentResult.data.activo,
            debe_cambiar_contrasena: currentResult.data.debe_cambiar_contrasena,
          }
        : null,
      error: null,
    };
  }

  if (currentResult.error.code !== '42703' && currentResult.error.code !== 'PGRST204') {
    return { profile: null, error: currentResult.error };
  }

  const legacyResult = await supabase
    .from('usuarios')
    .select('nombre_completo, rol')
    .eq('id', userId)
    .maybeSingle();

  return {
    profile: legacyResult.data
      ? {
          nombre_completo: legacyResult.data.nombre_completo,
          rol: legacyResult.data.rol,
          activo: true,
          debe_cambiar_contrasena: false,
        }
      : null,
    error: legacyResult.error,
  };
}

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
  const [mustChangePassword, setMustChangePassword] = useState(false);
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [passwordRecoverySent, setPasswordRecoverySent] = useState(false);

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

        const { profile, error: profileError } = await loadUserProfile(authSession.user.id);

        if (profileError) {
          await supabase.auth.signOut();
          localStorage.removeItem('sigpa_session');
          if (active) {
            setErrorMsg(`No se pudo consultar el perfil: ${profileError.message}`);
          }
          return;
        }
        if (!profile) {
          await supabase.auth.signOut();
          localStorage.removeItem('sigpa_session');
          if (active) {
            setErrorMsg('La cuenta inició sesión, pero no tiene un perfil vinculado en SIGPA. Contacta al Coordinador Académico para vincularla sin modificar el esquema de la base de datos.');
          }
          return;
        }
        if (!profile.activo) {
          await supabase.auth.signOut();
          localStorage.removeItem('sigpa_session');
          if (active) setErrorMsg('La cuenta está deshabilitada. Contacta al Coordinador Académico.');
          return;
        }

        if (active) {
          const forcePasswordChange = profile.debe_cambiar_contrasena === true;
          applySession({
            id: authSession.user.id,
            name: profile.nombre_completo || authSession.user.email || 'Usuario SIGPA',
            email: authSession.user.email ?? '',
            role: normalizeRole(profile.rol),
            token: authSession.access_token,
            mustChangePassword: forcePasswordChange,
          });
          setMustChangePassword(forcePasswordChange);
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
    setMustChangePassword(user.mustChangePassword === true);
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

      const { profile, error: profileError } = await loadUserProfile(authData.user.id);

      if (profileError) {
        await supabase.auth.signOut();
        setErrorMsg(`No se pudo consultar el perfil: ${profileError.message}`);
        return;
      }
      if (!profile) {
        await supabase.auth.signOut();
        setErrorMsg('La cuenta inició sesión, pero no tiene un perfil vinculado en SIGPA. Contacta al Coordinador Académico para vincularla sin modificar el esquema de la base de datos.');
        return;
      }
      if (!profile.activo) {
        await supabase.auth.signOut();
        setErrorMsg('La cuenta está deshabilitada. Contacta al Coordinador Académico.');
        return;
      }

      const forcePasswordChange = profile.debe_cambiar_contrasena === true;
      applySession({
        id: authData.user.id,
        name: profile.nombre_completo || authData.user.email || 'Usuario SIGPA',
        email: authData.user.email ?? email,
        role: normalizeRole(profile.rol),
        token: authData.session.access_token,
        mustChangePassword: forcePasswordChange,
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

  const handlePasswordChange = async (event: React.FormEvent) => {
    event.preventDefault();
    setErrorMsg(null);
    if (newPassword.length < 12 || !/[A-Z]/.test(newPassword) ||
        !/[a-z]/.test(newPassword) || !/\d/.test(newPassword) ||
        !/[^A-Za-z0-9]/.test(newPassword)) {
      setErrorMsg('Usa al menos 12 caracteres e incluye mayúscula, minúscula, número y símbolo.');
      return;
    }
    if (newPassword !== confirmPassword) {
      setErrorMsg('Las contraseñas no coinciden.');
      return;
    }

    setAuthLoading(true);
    try {
      const response = await apiFetch('/api/auth/password-changed', {
        method: 'POST',
        body: JSON.stringify({ contrasena: newPassword }),
      });
      if (!response.ok) throw new Error(await getApiError(response));
      setMustChangePassword(false);
      setNewPassword('');
      setConfirmPassword('');
      if (session) applySession({ ...session, mustChangePassword: false });
    } catch (changeError) {
      setErrorMsg(changeError instanceof Error ? changeError.message : 'No se pudo actualizar la contraseña.');
    } finally {
      setAuthLoading(false);
    }
  };

  const handlePasswordRecovery = async () => {
    setErrorMsg(null);
    setPasswordRecoverySent(false);
    if (!email.trim()) {
      setErrorMsg('Escribe tu correo institucional para solicitar el restablecimiento.');
      return;
    }
    setAuthLoading(true);
    try {
      const { error } = await supabase.auth.resetPasswordForEmail(email.trim());
      if (error) throw error;
      setPasswordRecoverySent(true);
    } catch (recoveryError) {
      setErrorMsg(recoveryError instanceof Error ? recoveryError.message : 'No se pudo solicitar el restablecimiento.');
    } finally {
      setAuthLoading(false);
    }
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

  if (session && mustChangePassword) {
    return (
      <div style={{ minHeight: '100vh', display: 'grid', placeItems: 'center', padding: '24px', background: '#020817', color: '#f8fafc', fontFamily: 'sans-serif' }}>
        <form onSubmit={handlePasswordChange} style={{ width: '100%', maxWidth: '480px', padding: '28px', borderRadius: '16px', background: '#111827', display: 'grid', gap: '14px' }}>
          <h1 style={{ margin: 0 }}>Cambia tu contraseña temporal</h1>
          <p style={{ color: '#94a3b8' }}>Por seguridad, debes crear una contraseña nueva antes de usar SIGPA.</p>
          {errorMsg && <p role="alert" style={{ color: '#fecaca' }}>{errorMsg}</p>}
          <label>Nueva contraseña
            <input required type="password" minLength={12} autoComplete="new-password" value={newPassword} onChange={(event) => setNewPassword(event.target.value)} />
          </label>
          <label>Confirmar contraseña
            <input required type="password" minLength={12} autoComplete="new-password" value={confirmPassword} onChange={(event) => setConfirmPassword(event.target.value)} />
          </label>
          <p>Al menos 12 caracteres con mayúscula, minúscula, número y símbolo.</p>
          <button type="submit" disabled={authLoading}>{authLoading ? 'Actualizando...' : 'Actualizar contraseña'}</button>
          <button type="button" disabled={authLoading} onClick={() => void handleLogout()}>Cerrar sesión</button>
        </form>
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
          {passwordRecoverySent && (
            <p role="status" style={{ color: '#bbf7d0', textAlign: 'center' }}>
              Si la cuenta existe, recibirás un correo con instrucciones para recuperar el acceso.
            </p>
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
          <button type="button" onClick={() => void handlePasswordRecovery()} disabled={authLoading} style={{ width: '100%', marginTop: '12px' }}>
            Olvidé mi contraseña
          </button>
        </form>
      </div>
    );
  }

  return <RoleDashboard user={session} onLogout={handleLogout} />;
}