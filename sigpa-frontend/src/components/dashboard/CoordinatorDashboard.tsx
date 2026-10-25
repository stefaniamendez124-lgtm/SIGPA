import { useEffect, useState } from 'react';
import { apiFetch, getApiError } from '../../utility/api';
import { UserManagement } from './UserManagement';

type CourseFilter = {
  id: string;
  codigo: string;
  nombre: string;
};
type TeacherFilter = { id: string; nombreCompleto: string };

type Kpis = {
  totalProyectos: number;
  entregasATiempo: number;
  entregasAtrasadas: number;
  pendientesAtrasadas: number;
  tasaEntregasATiempo: number;
  promedioGeneral: number;
  riesgosAbiertos: number;
  alertasCriticas: number;
  evaluacionesDisponibles: boolean;
  riesgosDisponibles: boolean;
};

const panelStyle = {
  backgroundColor: 'rgba(15, 23, 42, 0.8)',
  border: '1px solid rgba(148, 163, 184, 0.2)',
  borderRadius: '18px',
  padding: '20px',
};

export function CoordinatorDashboard() {
  const [courses, setCourses] = useState<CourseFilter[]>([]);
  const [teachers, setTeachers] = useState<TeacherFilter[]>([]);
  const [selectedCourse, setSelectedCourse] = useState('');
  const [selectedTeacher, setSelectedTeacher] = useState('');
  const [metrics, setMetrics] = useState<Kpis | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    const loadOptions = async () => {
      try {
        const response = await apiFetch('/api/analitica/opciones');
        if (!response.ok) throw new Error(await getApiError(response));
        const result = await response.json() as { cursos: CourseFilter[]; docentes: TeacherFilter[] };
        if (active) {
          setCourses(result.cursos);
          setTeachers(result.docentes);
        }
      } catch (loadError) {
        if (active) setError(loadError instanceof Error ? loadError.message : 'No se pudieron cargar los filtros.');
      }
    };
    void loadOptions();
    return () => { active = false; };
  }, []);

  useEffect(() => {
    let active = true;
    const loadMetrics = async () => {
      setLoading(true);
      setError(null);
      try {
        const query = new URLSearchParams();
        if (selectedCourse) query.set('cursoId', selectedCourse);
        if (selectedTeacher) query.set('docenteId', selectedTeacher);
        const queryString = query.toString();
        const suffix = queryString ? `?${queryString}` : '';
        const response = await apiFetch(`/api/analitica/kpis${suffix}`);
        if (!response.ok) throw new Error(await getApiError(response));
        const result = await response.json() as Kpis;
        if (active) setMetrics(result);
      } catch (loadError) {
        if (active) setError(loadError instanceof Error ? loadError.message : 'No se pudieron cargar los indicadores.');
      } finally {
        if (active) setLoading(false);
      }
    };

    void loadMetrics();
    const intervalId = window.setInterval(() => void loadMetrics(), 30_000);
    return () => {
      active = false;
      window.clearInterval(intervalId);
    };
  }, [selectedCourse, selectedTeacher]);

  return (
    <section aria-labelledby="coordinator-dashboard" style={{ display: 'grid', gap: '20px' }}>
      <div style={panelStyle}>
        <h2 id="coordinator-dashboard" style={{ margin: '0 0 14px' }}>Monitoreo académico global</h2>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '12px' }}>
          <label>
            Asignatura
            <select value={selectedCourse} onChange={(event) => setSelectedCourse(event.target.value)} style={{ display: 'block', width: '100%', marginTop: '6px' }}>
              <option value="">Todas las asignaturas</option>
              {courses.map((course) => (
                <option key={course.id} value={course.id}>{course.nombre} ({course.codigo})</option>
              ))}
            </select>
          </label>
          <label>
            Docente
            <select value={selectedTeacher} onChange={(event) => setSelectedTeacher(event.target.value)} style={{ display: 'block', width: '100%', marginTop: '6px' }}>
              <option value="">Todos los docentes</option>
              {teachers.map((teacher) => <option key={teacher.id} value={teacher.id}>{teacher.nombreCompleto}</option>)}
            </select>
          </label>
        </div>
        <p style={{ color: '#94a3b8', marginBottom: 0 }}>
          Actualización automática cada 30 segundos. Los indicadores de calificaciones y riesgos solo aparecen cuando esas tablas existen en el esquema actual.
        </p>
      </div>

      {error && <p role="alert" style={{ color: '#fecaca' }}>{error}</p>}
      {loading && <p role="status">Actualizando indicadores...</p>}
      {metrics && (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(190px, 1fr))', gap: '14px' }}>
          <Metric label="Proyectos" value={metrics.totalProyectos.toString()} />
          <Metric label="Entregas a tiempo" value={`${metrics.tasaEntregasATiempo}%`} detail={`${metrics.entregasATiempo} entregas`} />
          <Metric label="Entregas atrasadas" value={metrics.entregasAtrasadas.toString()} detail={`${metrics.pendientesAtrasadas} pendientes vencidas`} />
          <Metric
            label="Promedio publicado"
            value={metrics.evaluacionesDisponibles ? `${metrics.promedioGeneral}%` : 'No disponible'}
          />
          <Metric
            label="Riesgos abiertos"
            value={metrics.riesgosDisponibles ? metrics.riesgosAbiertos.toString() : 'No disponible'}
            detail={metrics.riesgosDisponibles ? `${metrics.alertasCriticas} de severidad alta o crítica` : undefined}
          />
        </div>
      )}
      <UserManagement />
    </section>
  );
}

function Metric({ label, value, detail }: { label: string; value: string; detail?: string }) {
  return (
    <article style={panelStyle}>
      <div style={{ color: '#94a3b8', fontSize: '13px' }}>{label}</div>
      <strong style={{ display: 'block', fontSize: '30px', marginTop: '6px' }}>{value}</strong>
      {detail && <div style={{ color: '#93c5fd', fontSize: '12px', marginTop: '4px' }}>{detail}</div>}
    </article>
  );
}
