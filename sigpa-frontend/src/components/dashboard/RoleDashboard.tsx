import { useEffect, useMemo, useState } from 'react';
import { supabase } from '../../supabaseClient';

type UserRole = 'Estudiante' | 'Profesor' | 'Coordinador' | 'Administrador';

type UserSession = {
  id: string;
  name: string;
  email: string;
  role: UserRole;
  token?: string;
};

type ProjectStatus = 'Pendiente' | 'En revisión' | 'Aprobado' | 'Rechazado' | 'Entregado' | 'En progreso';

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
  const [projectCourse, setProjectCourse] = useState('Ingeniería de requisitos');
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
      pendientes: teacherReviewProjects.filter((p) => p.status === 'Pendiente').length,
      enRevision: teacherReviewProjects.filter((p) => p.status === 'En revisión').length,
      aprobados: teacherReviewProjects.filter((p) => p.status === 'Aprobado').length,
    };
  }, [studentProjects, teacherReviewProjects, user.role]);

  const handleCreateProject = async (e: React.FormEvent) => {
    e.preventDefault();

    if (!projectName.trim()) {
      return;
    }

    setProjectSaving(true);
    setProjectError(null);
    const projectDate = new Date();
    projectDate.setDate(projectDate.getDate() + 14);
    const projectEnd = projectDate.toISOString();

    try {
      const { error } = await supabase.from('proyectos').insert({
        nombre: projectName,
        descripcion: projectDescription || 'Proyecto nuevo registrado en SIGPA.',
        curso: projectCourse,
        estado: 'Pendiente',
        progreso: 15,
        fecha_inicio: new Date().toISOString(),
        fecha_fin: projectEnd,
        fecha_entrega: projectDate.toISOString().slice(0, 10),
        creador_id: user.id,
      });

      if (error) {
        throw error;
      }

      setProjectName('');
      setProjectDescription('');
      setProjectCourse('Ingeniería de requisitos');
      await syncProjects();
    } catch (saveError) {
      setProjectError(`No se pudo guardar el proyecto: ${getErrorMessage(saveError)}`);
    } finally {
      setProjectSaving(false);
    }
  };

  const handleTeacherDecision = async (projectId: string, status: ProjectStatus) => {
    setProjectError(null);
    try {
      const { error } = await supabase
        .from('proyectos')
        .update({
          estado: status,
          progreso: status === 'Aprobado' ? 100 : 70,
          updated_at: new Date().toISOString(),
        })
        .eq('id', projectId);

      if (error) {
        throw error;
      }

      await syncProjects();
    } catch (updateError) {
      setProjectError(`No se pudo actualizar el proyecto: ${getErrorMessage(updateError)}`);
    }
  };

  return (
    <div style={{ minHeight: '100vh', background: 'linear-gradient(180deg, #020817 0%, #0f172a 100%)', color: '#f8fafc', fontFamily: 'sans-serif' }}>
      <header style={{ background: 'rgba(15, 23, 42, 0.8)', borderBottom: '1px solid rgba(148, 163, 184, 0.2)', padding: '18px 28px', display: 'flex', alignItems: 'center', justifyContent: 'space-between', position: 'sticky', top: 0, zIndex: 20, backdropFilter: 'blur(10px)' }}>
        <div>
          <div style={{ color: '#93c5fd', fontWeight: 700, letterSpacing: '0.08em', fontSize: '12px' }}>SIGPA / {user.role.toUpperCase()}</div>
          <div style={{ fontSize: '24px', fontWeight: 700 }}>{user.name}</div>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
          <div style={{ color: '#cbd5e1', fontSize: '14px' }}>{user.email}</div>
          <button onClick={onLogout} style={{ background: '#ef4444', border: 'none', borderRadius: '10px', color: '#fff', padding: '10px 16px', fontWeight: 700, cursor: 'pointer' }}>Cerrar sesión</button>
        </div>
      </header>

      <main style={{ padding: '28px', maxWidth: '1360px', margin: '0 auto' }}>
        {projectError && (
          <div role="alert" style={{ marginBottom: '20px', padding: '12px 16px', borderRadius: '10px', background: '#7f1d1d', color: '#fecaca' }}>
            {projectError}
          </div>
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
          <div style={{ display: 'grid', gridTemplateColumns: '1.1fr 1.4fr', gap: '28px' }}>
            <section style={{ ...cardStyle, padding: '24px' }}>
              <h3 style={{ margin: '0 0 18px', fontSize: '24px' }}>Ingresar proyecto</h3>
              <form onSubmit={handleCreateProject} style={{ display: 'grid', gap: '16px' }}>
                <div>
                  <label style={{ display: 'block', marginBottom: '8px', color: '#cbd5e1' }}>Nombre del proyecto</label>
                  <input value={projectName} onChange={(e) => setProjectName(e.target.value)} placeholder="Ej: Sistema de clínica virtual" style={{ width: '100%', boxSizing: 'border-box', background: '#020817', border: '1px solid #334155', borderRadius: '10px', color: '#fff', padding: '12px 14px' }} />
                </div>

                <div>
                  <label style={{ display: 'block', marginBottom: '8px', color: '#cbd5e1' }}>Asignatura</label>
                  <select value={projectCourse} onChange={(e) => setProjectCourse(e.target.value)} style={{ width: '100%', boxSizing: 'border-box', background: '#020817', border: '1px solid #334155', borderRadius: '10px', color: '#fff', padding: '12px 14px' }}>
                    <option>Ingeniería de requisitos</option>
                    <option>Metodologías ágiles</option>
                    <option>Proyecto integrador</option>
                    <option>Aplicaciones web</option>
                  </select>
                </div>

                <div>
                  <label style={{ display: 'block', marginBottom: '8px', color: '#cbd5e1' }}>Planteamiento del problema y objetivos</label>
                  <textarea value={projectDescription} onChange={(e) => setProjectDescription(e.target.value)} rows={5} placeholder="Describe la necesidad, la solución propuesta y los objetivos del proyecto..." style={{ width: '100%', boxSizing: 'border-box', resize: 'vertical', background: '#020817', border: '1px solid #334155', borderRadius: '10px', color: '#fff', padding: '12px 14px' }} />
                </div>

                <button type="submit" disabled={projectSaving} style={{ background: 'linear-gradient(135deg, #22c55e, #16a34a)', border: 'none', borderRadius: '10px', color: '#fff', fontWeight: 700, padding: '12px 16px', cursor: projectSaving ? 'wait' : 'pointer' }}>{projectSaving ? 'Guardando...' : 'Guardar proyecto'}</button>
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
                  </div>
                ))}
              </div>
            </section>
          </div>
        ) : (
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '28px' }}>
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
                      <button onClick={() => handleTeacherDecision(project.id, 'Aprobado')} style={{ background: '#16a34a', border: 'none', borderRadius: '8px', padding: '10px 12px', color: '#fff', fontWeight: 700, cursor: 'pointer' }}>Aprobar</button>
                      <button onClick={() => handleTeacherDecision(project.id, 'En revisión')} style={{ background: '#2563eb', border: 'none', borderRadius: '8px', padding: '10px 12px', color: '#fff', fontWeight: 700, cursor: 'pointer' }}>Revisar</button>
                      <button onClick={() => handleTeacherDecision(project.id, 'Pendiente')} style={{ background: '#475569', border: 'none', borderRadius: '8px', padding: '10px 12px', color: '#fff', fontWeight: 700, cursor: 'pointer' }}>Pendiente</button>
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
