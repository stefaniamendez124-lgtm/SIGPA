import { useEffect, useMemo, useState } from 'react';
import { ProjectWorkspace } from './ProjectWorkspace';
import { supabase } from '../../supabaseClient';
import { apiFetch, getApiError } from '../../utility/api';

type UserRole = 'Estudiante' | 'Profesor' | 'Coordinador' | 'Administrador';

type UserSession = {
  id: string;
  name: string;
  email: string;
  role: UserRole;
  token?: string;
};

type ProjectStatus = 'Pendiente' | 'Pendiente de Revisión' | 'En revisión' | 'Aprobado' | 'Rechazado' | 'Entregado' | 'En progreso';

type CourseOption = { id: string; codigo: string; nombre: string };
type StudentOption = { id: string; nombreCompleto: string; correoInstitucional: string };
type ProjectInvitation = { proyectoId: string; titulo: string; asignatura: string; invitadaAt: string };
type NotificationItem = { id: string; tipo: string; mensaje: string; creadaAt: string; leidaAt: string | null };

type ProjectItem = {
  id: string;
  title: string;
  description: string;
  course: string;
  status: ProjectStatus;
  progress: number;
  dueDate: string;
  members: string[];
  score?: number;
};

const getErrorMessage = (error: unknown): string => {
  if (error instanceof Error) {
    return error.message;
  }

  if (typeof error === 'object' && error !== null && 'message' in error && typeof error.message === 'string') {
    return error.message;
  }

  return 'error desconocido';
};

const cardStyle = {
  backgroundColor: 'rgba(15, 23, 42, 0.8)',
  border: '1px solid rgba(148, 163, 184, 0.2)',
  borderRadius: '18px',
  boxShadow: '0 14px 35px rgba(15, 23, 42, 0.35)',
};

export function RoleDashboard({ user, onLogout }: { user: UserSession; onLogout: () => void }) {
  const [studentProjects, setStudentProjects] = useState<ProjectItem[]>([]);
  const [teacherReviewProjects, setTeacherReviewProjects] = useState<ProjectItem[]>([]);
  const [projectName, setProjectName] = useState('');
  const [projectDescription, setProjectDescription] = useState('');
  const [projectObjective, setProjectObjective] = useState('');
  const [projectSpecificObjectives, setProjectSpecificObjectives] = useState('');
  const [courses, setCourses] = useState<CourseOption[]>([]);
  const [invitations, setInvitations] = useState<ProjectInvitation[]>([]);
  const [notifications, setNotifications] = useState<NotificationItem[]>([]);
  const [coursePeers, setCoursePeers] = useState<StudentOption[]>([]);
  const [selectedCourseId, setSelectedCourseId] = useState('');
  const [selectedMemberIds, setSelectedMemberIds] = useState<string[]>([]);
  const [coursesLoading, setCoursesLoading] = useState(true);
  const [projectsLoading, setProjectsLoading] = useState(true);
  const [projectSaving, setProjectSaving] = useState(false);
  const [projectError, setProjectError] = useState<string | null>(null);

  const syncProjects = async () => {
    setProjectsLoading(true);
    setProjectError(null);

    try {
      const { data, error } = await supabase
        .from('proyectos')
        .select('id, nombre, descripcion, curso, estado, progreso, fecha_entrega, creador_id, created_at')
        .order('created_at', { ascending: false });

      if (error) {
        throw error;
      }

      const mapped: ProjectItem[] = (data ?? []).map((project) => ({
        id: project.id,
        title: project.nombre,
        description: project.descripcion,
        course: project.curso,
        status: project.estado as ProjectStatus,
        progress: project.progreso,
        dueDate: project.fecha_entrega
          ? new Date(`${project.fecha_entrega}T00:00:00`).toLocaleDateString('es-ES')
          : 'Sin fecha',
        members: [project.creador_id === user.id ? user.name : 'Estudiante SIGPA'],
      }));

      if (user.role === 'Estudiante') {
        setStudentProjects(mapped);
      } else {
        setTeacherReviewProjects(mapped);
      }
    } catch (loadError) {
      setProjectError(`No se pudieron cargar los proyectos: ${getErrorMessage(loadError)}`);
    } finally {
      setProjectsLoading(false);
    }
  };

  useEffect(() => {
    void syncProjects();
  }, [user.id, user.role]);

  const syncInvitations = async () => {
    try {
      const response = await apiFetch('/api/proyectos/invitaciones/pendientes');
      if (!response.ok) throw new Error(await getApiError(response));
      setInvitations(await response.json() as ProjectInvitation[]);
    } catch (loadError) {
      setProjectError(`No se pudieron cargar las invitaciones: ${getErrorMessage(loadError)}`);
    }
  };

  const syncNotifications = async () => {
    try {
      const response = await apiFetch('/api/notificaciones');
      if (!response.ok) throw new Error(await getApiError(response));
      setNotifications(await response.json() as NotificationItem[]);
    } catch (loadError) {
      setProjectError(`No se pudieron cargar las notificaciones: ${getErrorMessage(loadError)}`);
    }
  };

  useEffect(() => {
    let active = true;
    const loadCourses = async () => {
      setCoursesLoading(true);
      try {
        const response = await apiFetch('/api/cursos/mis-matriculas');
        if (!response.ok) throw new Error(await getApiError(response));
        const enrolledCourses = await response.json() as CourseOption[];
        if (active) {
          setCourses(enrolledCourses);
          setSelectedCourseId((current) => current || enrolledCourses[0]?.id || '');
        }
      } catch (loadError) {
        if (active) setProjectError(`No se pudieron cargar las asignaturas matriculadas: ${getErrorMessage(loadError)}`);
      } finally {
        if (active) setCoursesLoading(false);
      }
    };
    if (user.role === 'Estudiante') void loadCourses();
    return () => { active = false; };
  }, [user.id, user.role]);

  useEffect(() => {
    if (user.role === 'Estudiante') void syncInvitations();
    void syncNotifications();
  }, [user.id, user.role]);

  useEffect(() => {
    let active = true;
    setSelectedMemberIds([]);
    setCoursePeers([]);
    if (!selectedCourseId) return () => { active = false; };

    const loadPeers = async () => {
      try {
        const response = await apiFetch(`/api/cursos/${selectedCourseId}/companeros`);
        if (!response.ok) throw new Error(await getApiError(response));
        const students = await response.json() as StudentOption[];
        if (active) setCoursePeers(students);
      } catch (loadError) {
        if (active) setProjectError(`No se pudieron cargar compañeros de asignatura: ${getErrorMessage(loadError)}`);
      }
    };
    void loadPeers();
    return () => { active = false; };
  }, [selectedCourseId]);

  const stats = useMemo(() => {
    if (user.role === 'Estudiante') {
      return {
        total: studentProjects.length,
        pendientes: studentProjects.filter((p) => p.status !== 'Aprobado' && p.status !== 'Entregado').length,
        aprobados: studentProjects.filter((p) => p.status === 'Aprobado').length,
        promedio: Math.round(
          studentProjects.reduce((sum, item) => sum + (item.score ?? 0), 0) / Math.max(studentProjects.length, 1),
        ),
      };
    }

    return {
      total: teacherReviewProjects.length,
      pendientes: teacherReviewProjects.filter((p) => p.status === 'Pendiente' || p.status === 'Pendiente de Revisión').length,
      enRevision: teacherReviewProjects.filter((p) => p.status === 'En revisión').length,
      aprobados: teacherReviewProjects.filter((p) => p.status === 'Aprobado').length,
    };
  }, [studentProjects, teacherReviewProjects, user.role]);

  const handleCreateProject = async (e: React.FormEvent) => {
    e.preventDefault();

    const specificObjectives = projectSpecificObjectives
      .split(/\r?\n/)
      .map((objective) => objective.trim())
      .filter(Boolean);

    if (!projectName.trim() || !projectDescription.trim() || !projectObjective.trim() ||
        specificObjectives.length === 0 || !selectedCourseId || selectedMemberIds.length < 1 || selectedMemberIds.length > 5) {
      setProjectError('Completa la propuesta, selecciona una asignatura matriculada e invita entre 1 y 5 compañeros.');
      return;
    }

    setProjectSaving(true);
    setProjectError(null);
    try {
      const response = await apiFetch('/api/projects', {
        method: 'POST',
        body: JSON.stringify({
          nombre: projectName.trim(),
          problema: projectDescription.trim(),
          objetivoGeneral: projectObjective.trim(),
          objetivosEspecificos: specificObjectives,
          cursoId: selectedCourseId,
          integrantesIds: selectedMemberIds,
        }),
      });

      if (!response.ok) {
        throw new Error(await getApiError(response));
      }

      setProjectName('');
      setProjectDescription('');
      setProjectObjective('');
      setProjectSpecificObjectives('');
      setSelectedMemberIds([]);
      await syncProjects();
    } catch (saveError) {
      setProjectError(`No se pudo guardar el proyecto: ${getErrorMessage(saveError)}`);
    } finally {
      setProjectSaving(false);
    }
  };

  const handleTeacherDecision = async (projectId: string, status: 'Aprobado' | 'Rechazado') => {
    setProjectError(null);
    try {
      const comentarios = status === 'Rechazado'
        ? window.prompt('Indica las observaciones para que el equipo pueda corregir la propuesta:')
        : '';
      if (status === 'Rechazado' && !comentarios?.trim()) return;

      const response = await apiFetch(`/api/proyectos/${projectId}/evaluar`, {
        method: 'PATCH',
        body: JSON.stringify({ estado: status, comentarios }),
      });

      if (!response.ok) {
        throw new Error(await getApiError(response));
      }

      await syncProjects();
    } catch (updateError) {
      setProjectError(`No se pudo actualizar el proyecto: ${getErrorMessage(updateError)}`);
    }
  };

  const respondToInvitation = async (projectId: string, accept: boolean) => {
    setProjectError(null);
    try {
      const action = accept ? 'aceptar' : 'rechazar';
      const response = await apiFetch(`/api/proyectos/${projectId}/invitaciones/${action}`, { method: 'POST' });
      if (!response.ok) throw new Error(await getApiError(response));
      await syncInvitations();
      await syncProjects();
    } catch (responseError) {
      setProjectError(`No se pudo responder a la invitación: ${getErrorMessage(responseError)}`);
    }
  };

  return (
    <div style={{ minHeight: '100vh', background: 'linear-gradient(180deg, #020817 0%, #0f172a 100%)', color: '#f8fafc', fontFamily: 'sans-serif' }}>
      <header style={{ background: 'rgba(15, 23, 42, 0.8)', borderBottom: '1px solid rgba(148, 163, 184, 0.2)', padding: '18px 28px', display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: '12px', position: 'sticky', top: 0, zIndex: 20, backdropFilter: 'blur(10px)' }}>
        <div>
          <div style={{ color: '#93c5fd', fontWeight: 700, letterSpacing: '0.08em', fontSize: '12px' }}>SIGPA / {user.role.toUpperCase()}</div>
          <div style={{ fontSize: '24px', fontWeight: 700 }}>{user.name}</div>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
          <div style={{ color: '#cbd5e1', fontSize: '14px' }}>{user.email}</div>
          <button onClick={onLogout} style={{ background: '#ef4444', border: 'none', borderRadius: '10px', color: '#fff', padding: '10px 16px', fontWeight: 700, cursor: 'pointer' }}>Cerrar sesión</button>
        </div>
      </header>

      <main style={{ padding: 'clamp(16px, 4vw, 28px)', maxWidth: '1360px', margin: '0 auto' }}>
        {projectError && (
          <div role="alert" style={{ marginBottom: '20px', padding: '12px 16px', borderRadius: '10px', background: '#7f1d1d', color: '#fecaca' }}>
            {projectError}
          </div>
        )}
        {notifications.length > 0 && (
          <section aria-labelledby="notifications-heading" style={{ ...cardStyle, padding: '16px 20px', marginBottom: '24px' }}>
            <h2 id="notifications-heading" style={{ margin: '0 0 10px', fontSize: '20px' }}>Notificaciones</h2>
            <ul style={{ display: 'grid', gap: '8px', margin: 0, paddingLeft: '20px' }}>
              {notifications.slice(0, 5).map((notification) => (
                <li key={notification.id}>
                  <span>{notification.mensaje}</span>
                  <time dateTime={notification.creadaAt} style={{ marginLeft: '8px', color: '#94a3b8' }}>
                    {new Date(notification.creadaAt).toLocaleString('es-ES')}
                  </time>
                </li>
              ))}
            </ul>
          </section>
        )}
        <section style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: '18px', marginBottom: '28px' }}>
          {user.role === 'Estudiante' ? (
            <>
              <MetricCard label="Proyectos" value={String(stats.total)} hint="activos" />
              <MetricCard label="Pendientes" value={String(stats.pendientes)} hint="por entregar" />
              <MetricCard label="Aprobados" value={String(stats.aprobados)} hint="ya evaluados" />
              <MetricCard label="Promedio" value={`${stats.promedio}%`} hint="general" />
            </>
          ) : (
            <>
              <MetricCard label="Proyectos" value={String(stats.total)} hint="asignados" />
              <MetricCard label="Por revisar" value={String(stats.pendientes)} hint="pendientes" />
              <MetricCard label="En revisión" value={String(stats.enRevision)} hint="en seguimiento" />
              <MetricCard label="Aprobados" value={String(stats.aprobados)} hint="finalizados" />
            </>
          )}
        </section>

        {user.role === 'Estudiante' ? (
          <>
          {invitations.length > 0 && (
            <section aria-labelledby="pending-invitations" style={{ ...cardStyle, padding: '20px', marginBottom: '24px' }}>
              <h2 id="pending-invitations" style={{ marginTop: 0 }}>Invitaciones pendientes</h2>
              <ul style={{ display: 'grid', gap: '12px', paddingLeft: '20px' }}>
                {invitations.map((invitation) => (
                  <li key={invitation.proyectoId} style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px', flexWrap: 'wrap' }}>
                    <span>{invitation.titulo} — {invitation.asignatura}</span>
                    <span style={{ display: 'flex', gap: '8px' }}>
                      <button type="button" onClick={() => void respondToInvitation(invitation.proyectoId, true)}>Aceptar</button>
                      <button type="button" onClick={() => void respondToInvitation(invitation.proyectoId, false)}>Rechazar</button>
                    </span>
                  </li>
                ))}
              </ul>
            </section>
          )}
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 360px), 1fr))', gap: '28px' }}>
            <section style={{ ...cardStyle, padding: '24px' }}>
              <h3 style={{ margin: '0 0 18px', fontSize: '24px' }}>Ingresar proyecto</h3>
              <form onSubmit={handleCreateProject} style={{ display: 'grid', gap: '16px' }}>
                <div>
                  <label htmlFor="project-title" style={{ display: 'block', marginBottom: '8px', color: '#cbd5e1' }}>Título del proyecto</label>
                  <input id="project-title" value={projectName} onChange={(e) => setProjectName(e.target.value)} required maxLength={200} placeholder="Ej: Sistema de clínica virtual" style={{ width: '100%', boxSizing: 'border-box', background: '#020817', border: '1px solid #334155', borderRadius: '10px', color: '#fff', padding: '12px 14px' }} />
                </div>

                <div>
                  <label htmlFor="project-course" style={{ display: 'block', marginBottom: '8px', color: '#cbd5e1' }}>Asignatura con matrícula activa</label>
                  <select id="project-course" required value={selectedCourseId} onChange={(e) => setSelectedCourseId(e.target.value)} style={{ width: '100%', boxSizing: 'border-box', background: '#020817', border: '1px solid #334155', borderRadius: '10px', color: '#fff', padding: '12px 14px' }}>
                    <option value="">{coursesLoading ? 'Cargando asignaturas...' : 'Selecciona una asignatura'}</option>
                    {courses.map((course) => <option key={course.id} value={course.id}>{course.nombre} ({course.codigo})</option>)}
                  </select>
                </div>

                <div>
                  <label htmlFor="project-problem" style={{ display: 'block', marginBottom: '8px', color: '#cbd5e1' }}>Planteamiento del problema</label>
                  <textarea id="project-problem" value={projectDescription} onChange={(e) => setProjectDescription(e.target.value)} required maxLength={10000} rows={4} placeholder="Describe el problema que el proyecto busca resolver..." style={{ width: '100%', boxSizing: 'border-box', resize: 'vertical', background: '#020817', border: '1px solid #334155', borderRadius: '10px', color: '#fff', padding: '12px 14px' }} />
                </div>

                <div>
                  <label htmlFor="project-objective" style={{ display: 'block', marginBottom: '8px', color: '#cbd5e1' }}>Objetivo general</label>
                  <textarea id="project-objective" value={projectObjective} onChange={(e) => setProjectObjective(e.target.value)} required maxLength={4000} rows={2} placeholder="Describe el resultado principal esperado..." style={{ width: '100%', boxSizing: 'border-box', resize: 'vertical', background: '#020817', border: '1px solid #334155', borderRadius: '10px', color: '#fff', padding: '12px 14px' }} />
                </div>

                <div>
                  <label htmlFor="project-specific-objectives" style={{ display: 'block', marginBottom: '8px', color: '#cbd5e1' }}>Objetivos específicos (uno por línea)</label>
                  <textarea id="project-specific-objectives" value={projectSpecificObjectives} onChange={(e) => setProjectSpecificObjectives(e.target.value)} required rows={3} placeholder={'Identificar las necesidades...\nDiseñar la solución...'} style={{ width: '100%', boxSizing: 'border-box', resize: 'vertical', background: '#020817', border: '1px solid #334155', borderRadius: '10px', color: '#fff', padding: '12px 14px' }} />
                </div>

                <fieldset style={{ display: 'grid', gap: '8px', border: '1px solid #334155', borderRadius: '10px', padding: '12px' }}>
                  <legend>Invita compañeros de la asignatura (1–5)</legend>
                  {coursePeers.length === 0
                    ? <p role="status" style={{ color: '#94a3b8' }}>No hay compañeros disponibles con matrícula activa y sin equipo en esta asignatura.</p>
                    : coursePeers.map((peer) => (
                      <label key={peer.id} style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <input
                          type="checkbox"
                          checked={selectedMemberIds.includes(peer.id)}
                          disabled={!selectedMemberIds.includes(peer.id) && selectedMemberIds.length >= 5}
                          onChange={(event) => setSelectedMemberIds((current) =>
                            event.target.checked ? [...current, peer.id] : current.filter((id) => id !== peer.id))}
                        />
                        {peer.nombreCompleto} ({peer.correoInstitucional})
                      </label>
                    ))}
                  <span aria-live="polite">{selectedMemberIds.length} de 5 compañeros seleccionados</span>
                </fieldset>

                <button type="submit" disabled={projectSaving || coursesLoading || courses.length === 0 || selectedMemberIds.length === 0} style={{ background: 'linear-gradient(135deg, #22c55e, #16a34a)', border: 'none', borderRadius: '10px', color: '#fff', fontWeight: 700, padding: '12px 16px', cursor: projectSaving ? 'wait' : 'pointer' }}>{projectSaving ? 'Guardando...' : 'Registrar proyecto e invitar equipo'}</button>
              </form>
            </section>

            <section style={{ ...cardStyle, padding: '24px' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '18px' }}>
                <h3 style={{ margin: 0, fontSize: '24px' }}>Mis proyectos</h3>
                <span style={{ color: '#93c5fd', fontSize: '14px' }}>Estado global</span>
              </div>

              <div style={{ display: 'grid', gap: '16px' }}>
                {projectsLoading ? <p>Cargando proyectos de Supabase...</p> : studentProjects.length === 0 ? <p style={{ color: '#94a3b8' }}>Todavía no tienes proyectos registrados.</p> : studentProjects.map((project) => (
                  <div key={project.id} style={{ border: '1px solid rgba(148, 163, 184, 0.2)', borderRadius: '16px', background: '#020817', padding: '18px' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', gap: '12px', alignItems: 'flex-start' }}>
                      <div>
                        <div style={{ fontSize: '20px', fontWeight: 700 }}>{project.title}</div>
                        <div style={{ color: '#94a3b8', marginTop: '4px' }}>{project.course}</div>
                      </div>
                      <span style={{ padding: '6px 10px', borderRadius: '999px', fontSize: '12px', fontWeight: 700, background: 'rgba(125, 211, 252, 0.16)', color: '#bae6fd', border: '1px solid rgba(125, 211, 252, 0.32)' }}>{project.status}</span>
                    </div>

                    <p style={{ color: '#cbd5e1', lineHeight: 1.6, margin: '14px 0' }}>{project.description}</p>

                    <div style={{ display: 'flex', justifyContent: 'space-between', gap: '18px', color: '#cbd5e1', fontSize: '13px', marginBottom: '12px' }}>
                      <span>Miembros: {project.members.join(', ')}</span>
                      <span>Entrega: {project.dueDate}</span>
                    </div>

                    <div style={{ marginBottom: '8px', color: '#94a3b8', fontSize: '12px' }}>Avance del proyecto: {project.progress}%</div>
                    <div style={{ height: '10px', borderRadius: '999px', background: '#1e293b', overflow: 'hidden' }}>
                      <div style={{ width: `${project.progress}%`, height: '100%', background: 'linear-gradient(90deg, #38bdf8, #22c55e)' }} />
                    </div>
                    <ProjectWorkspace projectId={project.id} />
                  </div>
                ))}
              </div>
            </section>
          </div>
          </>
        ) : (
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 360px), 1fr))', gap: '28px' }}>
            <section style={{ ...cardStyle, padding: '24px' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '18px', alignItems: 'center' }}>
                <h3 style={{ margin: 0, fontSize: '24px' }}>Proyectos por revisar</h3>
                <span style={{ color: '#93c5fd', fontSize: '14px' }}>Panel docente</span>
              </div>

              <div style={{ display: 'grid', gap: '16px' }}>
                {projectsLoading ? <p>Cargando proyectos de Supabase...</p> : teacherReviewProjects.length === 0 ? <p style={{ color: '#94a3b8' }}>No hay proyectos registrados para revisar.</p> : teacherReviewProjects.map((project) => (
                  <div key={project.id} style={{ border: '1px solid rgba(148, 163, 184, 0.2)', borderRadius: '16px', background: '#020817', padding: '18px' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', gap: '12px', alignItems: 'flex-start' }}>
                      <div>
                        <div style={{ fontSize: '20px', fontWeight: 700 }}>{project.title}</div>
                        <div style={{ color: '#94a3b8', marginTop: '4px' }}>{project.course}</div>
                      </div>
                      <span style={{ padding: '6px 10px', borderRadius: '999px', fontSize: '12px', fontWeight: 700, background: 'rgba(251, 191, 36, 0.12)', color: '#fcd34d', border: '1px solid rgba(251, 191, 36, 0.32)' }}>{project.status}</span>
                    </div>

                    <p style={{ color: '#cbd5e1', lineHeight: 1.6, margin: '12px 0' }}>{project.description}</p>

                    <div style={{ display: 'flex', justifyContent: 'space-between', color: '#cbd5e1', fontSize: '13px', marginBottom: '12px' }}>
                      <span>Equipo: {project.members.join(', ')}</span>
                      <span>Fecha: {project.dueDate}</span>
                    </div>

                    <div style={{ marginBottom: '8px', color: '#94a3b8', fontSize: '12px' }}>Avance: {project.progress}%</div>
                    <div style={{ height: '10px', borderRadius: '999px', background: '#1e293b', overflow: 'hidden', marginBottom: '16px' }}>
                      <div style={{ width: `${project.progress}%`, height: '100%', background: 'linear-gradient(90deg, #f59e0b, #f97316)' }} />
                    </div>

                    <div style={{ display: 'flex', gap: '10px', flexWrap: 'wrap' }}>
                      {(project.status === 'Pendiente' || project.status === 'Pendiente de Revisión' || project.status === 'En revisión') && (
                        <>
                          <button onClick={() => void handleTeacherDecision(project.id, 'Aprobado')} style={{ background: '#16a34a', border: 'none', borderRadius: '8px', padding: '10px 12px', color: '#fff', fontWeight: 700, cursor: 'pointer' }}>Aprobar</button>
                          <button onClick={() => void handleTeacherDecision(project.id, 'Rechazado')} style={{ background: '#b91c1c', border: 'none', borderRadius: '8px', padding: '10px 12px', color: '#fff', fontWeight: 700, cursor: 'pointer' }}>Rechazar con observaciones</button>
                        </>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            </section>

            <section style={{ ...cardStyle, padding: '24px' }}>
              <h3 style={{ margin: '0 0 16px', fontSize: '24px' }}>Indicadores del curso</h3>

              <div style={{ display: 'grid', gap: '14px' }}>
                <InsightCard title="Rúbricas" value="8" detail="criterios activos" accent="#8b5cf6" />
                <InsightCard title="Entregables" value="26" detail="sin retrasos críticos" accent="#22c55e" />
                <InsightCard title="Alertas" value="3" detail="proyectos con riesgo" accent="#f59e0b" />
                <InsightCard title="Promedio" value="87%" detail="calificación general" accent="#38bdf8" />
              </div>
            </section>
          </div>
        )}
      </main>
    </div>
  );
}

function MetricCard({ label, value, hint }: { label: string; value: string; hint: string }) {
  return (
    <div style={{ ...cardStyle, padding: '20px 18px' }}>
      <div style={{ color: '#94a3b8', fontSize: '13px', marginBottom: '8px' }}>{label}</div>
      <div style={{ fontSize: '32px', fontWeight: 700 }}>{value}</div>
      <div style={{ marginTop: '6px', color: '#93c5fd', fontSize: '12px' }}>{hint}</div>
    </div>
  );
}

function InsightCard({ title, value, detail, accent }: { title: string; value: string; detail: string; accent: string }) {
  return (
    <div style={{ border: '1px solid rgba(148, 163, 184, 0.2)', borderRadius: '14px', background: '#020817', padding: '16px 18px' }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <div>
          <div style={{ color: '#cbd5e1', fontSize: '14px' }}>{title}</div>
          <div style={{ fontSize: '28px', fontWeight: 700 }}>{value}</div>
        </div>
        <div style={{ width: '14px', height: '14px', borderRadius: '999px', background: accent, boxShadow: `0 0 0 8px ${accent}22` }} />
      </div>
      <div style={{ marginTop: '10px', color: '#94a3b8', fontSize: '13px' }}>{detail}</div>
    </div>
  );
}
