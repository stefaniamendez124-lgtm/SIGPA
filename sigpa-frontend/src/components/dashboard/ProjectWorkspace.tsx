import { ChangeEvent, CSSProperties, DragEvent, FormEvent, useEffect, useState } from 'react';
import { apiFetch, getApiError } from '../../utility/api';

type Milestone = {
  id: string;
  titulo: string;
  fechaLimite: string;
  estado: string;
  permiteEntregaExtemporanea: boolean;
};

type TeamMember = {
  id: string;
  nombre: string;
};

type KanbanTask = {
  id: string;
  titulo: string;
  descripcion: string;
  fechaVencimiento: string | null;
  responsableId: string;
  estado: 'Por Hacer' | 'En Proceso' | 'Hecho';
};

type TaskForm = {
  titulo: string;
  descripcion: string;
  responsableId: string;
  fechaVencimiento: string;
};

type EditableTaskForm = TaskForm & { estado: KanbanTask['estado'] };

const columns: KanbanTask['estado'][] = ['Por Hacer', 'En Proceso', 'Hecho'];
const maxFileSize = 25 * 1024 * 1024;
const inputStyle: CSSProperties = {
  boxSizing: 'border-box',
  width: '100%',
  border: '1px solid #475569',
  borderRadius: '8px',
  padding: '10px',
  background: '#020817',
  color: '#f8fafc',
};

function emptyTaskForm(): TaskForm {
  return { titulo: '', descripcion: '', responsableId: '', fechaVencimiento: '' };
}

export function ProjectWorkspace({ projectId }: { projectId: string }) {
  const [milestones, setMilestones] = useState<Milestone[]>([]);
  const [members, setMembers] = useState<TeamMember[]>([]);
  const [tasks, setTasks] = useState<KanbanTask[]>([]);
  const [selectedMilestone, setSelectedMilestone] = useState('');
  const [evidenceType, setEvidenceType] = useState<'file' | 'url'>('file');
  const [file, setFile] = useState<File | null>(null);
  const [url, setUrl] = useState('');
  const [taskForm, setTaskForm] = useState<TaskForm>(emptyTaskForm);
  const [editingTaskId, setEditingTaskId] = useState<string | null>(null);
  const [editingTask, setEditingTask] = useState<EditableTaskForm>({ ...emptyTaskForm(), estado: 'Por Hacer' });
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const loadWorkspace = async () => {
    setLoading(true);
    setError(null);
    try {
      const [milestonesResponse, membersResponse, tasksResponse] = await Promise.all([
        apiFetch(`/api/projects/${projectId}/milestones`),
        apiFetch(`/api/projects/${projectId}/members`),
        apiFetch(`/api/projects/${projectId}/tasks`),
      ]);
      for (const response of [milestonesResponse, membersResponse, tasksResponse]) {
        if (!response.ok) throw new Error(await getApiError(response));
      }
      const [milestoneData, memberData, taskData] = await Promise.all([
        milestonesResponse.json() as Promise<Milestone[]>,
        membersResponse.json() as Promise<TeamMember[]>,
        tasksResponse.json() as Promise<KanbanTask[]>,
      ]);
      setMilestones(milestoneData);
      setMembers(memberData);
      setTasks(taskData);
      setSelectedMilestone((current) =>
        milestoneData.some((item) => item.id === current && item.estado === 'Pendiente')
          ? current
          : milestoneData.find((item) => item.estado === 'Pendiente')?.id ?? '',
      );
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'No se pudo cargar el espacio del proyecto.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadWorkspace();
  }, [projectId]);

  const selectFile = (selected: File | undefined) => {
    setError(null);
    if (!selected) return;
    if (!/\.(pdf|zip)$/i.test(selected.name)) {
      setFile(null);
      setError('Solo se permiten archivos PDF o ZIP.');
      return;
    }
    if (selected.size > maxFileSize) {
      setFile(null);
      setError('El archivo supera el límite máximo de 25 MB.');
      return;
    }
    setFile(selected);
  };

  const handleFileInput = (event: ChangeEvent<HTMLInputElement>) => {
    selectFile(event.target.files?.[0]);
  };

  const handleDrop = (event: DragEvent<HTMLLabelElement>) => {
    event.preventDefault();
    selectFile(event.dataTransfer.files[0]);
  };

  const submitDeliverable = async (event: FormEvent) => {
    event.preventDefault();
    if (!selectedMilestone) {
      setError('Selecciona un hito pendiente.');
      return;
    }
    if (evidenceType === 'file' && !file) {
      setError('Selecciona un archivo PDF o ZIP.');
      return;
    }
    if (evidenceType === 'url' && !url.trim()) {
      setError('Ingresa un enlace de GitHub o Google Drive.');
      return;
    }

    setSaving(true);
    setError(null);
    setMessage(null);
    try {
      const body = new FormData();
      body.set('hitoId', selectedMilestone);
      if (evidenceType === 'file' && file) body.set('archivo', file);
      if (evidenceType === 'url') body.set('url', url.trim());

      const response = await apiFetch(`/api/projects/${projectId}/deliverables`, {
        method: 'POST',
        body,
      });
      if (!response.ok) throw new Error(await getApiError(response));
      const result = await response.json() as { estado: string; selloTiempoUtc: string };
      setMessage(`Entrega registrada: ${result.estado}. Sello UTC: ${new Date(result.selloTiempoUtc).toLocaleString('es-ES')}.`);
      setFile(null);
      setUrl('');
      await loadWorkspace();
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : 'No se pudo registrar la entrega.');
    } finally {
      setSaving(false);
    }
  };

  const submitTask = async (event: FormEvent) => {
    event.preventDefault();
    setSaving(true);
    setError(null);
    try {
      const response = await apiFetch(`/api/projects/${projectId}/tasks`, {
        method: 'POST',
        body: JSON.stringify({
          titulo: taskForm.titulo,
          descripcion: taskForm.descripcion,
          responsableId: taskForm.responsableId,
          fechaVencimiento: taskForm.fechaVencimiento
            ? new Date(taskForm.fechaVencimiento).toISOString()
            : null,
        }),
      });
      if (!response.ok) throw new Error(await getApiError(response));
      setTaskForm(emptyTaskForm());
      await loadWorkspace();
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : 'No se pudo crear la tarea.');
    } finally {
      setSaving(false);
    }
  };

  const saveTask = async (task: KanbanTask, changes: Partial<TaskForm> & { estado?: KanbanTask['estado'] }) => {
    const next = { ...task, ...changes };
    setError(null);
    try {
      const response = await apiFetch(`/api/kanban/tasks/${task.id}`, {
        method: 'PUT',
        body: JSON.stringify({
          titulo: next.titulo,
          descripcion: next.descripcion,
          responsableId: next.responsableId,
          fechaVencimiento: next.fechaVencimiento
            ? new Date(next.fechaVencimiento).toISOString()
            : null,
          estado: next.estado,
        }),
      });
      if (!response.ok) throw new Error(await getApiError(response));
      setEditingTaskId(null);
      await loadWorkspace();
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : 'No se pudo actualizar la tarea.');
    }
  };

  const deleteTask = async (taskId: string) => {
    setError(null);
    try {
      const response = await apiFetch(`/api/kanban/tasks/${taskId}`, { method: 'DELETE' });
      if (!response.ok) throw new Error(await getApiError(response));
      await loadWorkspace();
    } catch (deleteError) {
      setError(deleteError instanceof Error ? deleteError.message : 'No se pudo eliminar la tarea.');
    }
  };

  if (loading) return <p role="status">Cargando entregas y tablero...</p>;

  return (
    <section aria-label="Entregas y tablero de tareas" style={{ display: 'grid', gap: '18px', marginTop: '20px' }}>
      {error && <p role="alert" style={{ color: '#fecaca' }}>{error}</p>}
      {message && <p role="status" style={{ color: '#bbf7d0' }}>{message}</p>}

      <section aria-labelledby={`deliverables-${projectId}`} style={{ borderTop: '1px solid #334155', paddingTop: '16px' }}>
        <h4 id={`deliverables-${projectId}`} style={{ margin: '0 0 12px' }}>Hitos y entregas</h4>
        <form onSubmit={submitDeliverable} style={{ display: 'grid', gap: '10px' }}>
          <label>
            Hito pendiente
            <select
              required
              value={selectedMilestone}
              onChange={(event) => setSelectedMilestone(event.target.value)}
              style={inputStyle}
            >
              <option value="">Selecciona un hito</option>
              {milestones.filter((item) => item.estado === 'Pendiente').map((item) => (
                <option key={item.id} value={item.id}>
                  {item.titulo} — vence {new Date(item.fechaLimite).toLocaleString('es-ES')}
                </option>
              ))}
            </select>
          </label>
          <fieldset style={{ border: 0, padding: 0, margin: 0 }}>
            <legend>Tipo de evidencia</legend>
            <label style={{ marginRight: '16px' }}>
              <input type="radio" name={`evidence-${projectId}`} checked={evidenceType === 'file'} onChange={() => setEvidenceType('file')} /> Archivo
            </label>
            <label>
              <input type="radio" name={`evidence-${projectId}`} checked={evidenceType === 'url'} onChange={() => setEvidenceType('url')} /> Enlace
            </label>
          </fieldset>
          {evidenceType === 'file' ? (
            <label
              onDragOver={(event) => event.preventDefault()}
              onDrop={handleDrop}
              style={{ display: 'block', border: '2px dashed #64748b', borderRadius: '10px', padding: '16px', textAlign: 'center' }}
            >
              Arrastra aquí o selecciona un archivo PDF/ZIP (máximo 25 MB).
              <input type="file" accept=".pdf,.zip,application/pdf,application/zip" onChange={handleFileInput} style={{ display: 'block', marginTop: '10px', width: '100%' }} />
              <span aria-live="polite">
                {file ? `${file.name} — ${(file.size / (1024 * 1024)).toFixed(2)} MB` : 'Ningún archivo seleccionado'}
              </span>
            </label>
          ) : (
            <label>
              URL de GitHub o Google Drive
              <input type="url" value={url} onChange={(event) => setUrl(event.target.value)} placeholder="https://github.com/..." style={inputStyle} />
            </label>
          )}
          <button disabled={saving || milestones.every((item) => item.estado !== 'Pendiente')} type="submit">
            {saving ? 'Enviando...' : 'Enviar entrega'}
          </button>
        </form>
      </section>

      <section aria-labelledby={`kanban-${projectId}`} style={{ borderTop: '1px solid #334155', paddingTop: '16px' }}>
        <h4 id={`kanban-${projectId}`} style={{ margin: '0 0 12px' }}>Tablero Kanban</h4>
        <form onSubmit={submitTask} style={{ display: 'grid', gap: '10px', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', marginBottom: '16px' }}>
          <label>
            Título
            <input required value={taskForm.titulo} onChange={(event) => setTaskForm({ ...taskForm, titulo: event.target.value })} style={inputStyle} />
          </label>
          <label>
            Descripción
            <input value={taskForm.descripcion} onChange={(event) => setTaskForm({ ...taskForm, descripcion: event.target.value })} style={inputStyle} />
          </label>
          <label>
            Responsable (obligatorio)
            <select required value={taskForm.responsableId} onChange={(event) => setTaskForm({ ...taskForm, responsableId: event.target.value })} style={inputStyle}>
              <option value="">Selecciona integrante</option>
              {members.map((member) => <option key={member.id} value={member.id}>{member.nombre}</option>)}
            </select>
          </label>
          <label>
            Fecha de compromiso
            <input type="date" value={taskForm.fechaVencimiento} onChange={(event) => setTaskForm({ ...taskForm, fechaVencimiento: event.target.value })} style={inputStyle} />
          </label>
          <button disabled={saving || members.length === 0} type="submit">Crear tarea</button>
        </form>

        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '12px' }}>
          {columns.map((column) => (
            <section key={column} aria-label={column} style={{ minHeight: '100px', padding: '12px', background: '#0f172a', borderRadius: '10px' }}>
              <h5 style={{ margin: '0 0 10px' }}>{column}</h5>
              {tasks.filter((task) => task.estado === column).map((task) => {
                const responsible = members.find((member) => member.id === task.responsableId)?.nombre ?? 'Integrante';
                const isEditing = editingTaskId === task.id;
                return (
                  <article key={task.id} style={{ marginBottom: '10px', padding: '12px', borderRadius: '8px', background: '#1e293b' }}>
                    {isEditing ? (
                      <form onSubmit={(event) => { event.preventDefault(); void saveTask(task, editingTask); }} style={{ display: 'grid', gap: '8px' }}>
                        <label>Título<input required value={editingTask.titulo} onChange={(event) => setEditingTask({ ...editingTask, titulo: event.target.value })} style={inputStyle} /></label>
                        <label>Descripción<input value={editingTask.descripcion} onChange={(event) => setEditingTask({ ...editingTask, descripcion: event.target.value })} style={inputStyle} /></label>
                        <label>Responsable<select required value={editingTask.responsableId} onChange={(event) => setEditingTask({ ...editingTask, responsableId: event.target.value })} style={inputStyle}>
                          {members.map((member) => <option key={member.id} value={member.id}>{member.nombre}</option>)}
                        </select></label>
                        <label>Fecha de compromiso<input type="date" value={editingTask.fechaVencimiento?.slice(0, 10) ?? ''} onChange={(event) => setEditingTask({ ...editingTask, fechaVencimiento: event.target.value })} style={inputStyle} /></label>
                        <label>Columna<select value={editingTask.estado} onChange={(event) => setEditingTask({ ...editingTask, estado: event.target.value as KanbanTask['estado'] })} style={inputStyle}>
                          {columns.map((status) => <option key={status}>{status}</option>)}
                        </select></label>
                        <button type="submit">Guardar cambios</button>
                        <button type="button" onClick={() => setEditingTaskId(null)}>Cancelar</button>
                      </form>
                    ) : (
                      <>
                        <strong>{task.titulo}</strong>
                        {task.descripcion && <p>{task.descripcion}</p>}
                        <p>Responsable: {responsible}</p>
                        <p>Compromiso: {task.fechaVencimiento ? new Date(task.fechaVencimiento).toLocaleDateString('es-ES') : 'Sin fecha'}</p>
                        <label>
                          Mover tarea
                          <select
                            value={task.estado}
                            onChange={(event) => void saveTask(task, { estado: event.target.value as KanbanTask['estado'] })}
                            style={inputStyle}
                          >
                            {columns.map((status) => <option key={status}>{status}</option>)}
                          </select>
                        </label>
                        <div style={{ display: 'flex', gap: '8px', marginTop: '8px' }}>
                          <button type="button" onClick={() => {
                            setEditingTaskId(task.id);
                            setEditingTask({
                              titulo: task.titulo,
                              descripcion: task.descripcion,
                              responsableId: task.responsableId,
                              fechaVencimiento: task.fechaVencimiento?.slice(0, 10) ?? '',
                              estado: task.estado,
                            });
                          }}>Editar</button>
                          <button type="button" onClick={() => void deleteTask(task.id)}>Eliminar</button>
                        </div>
                      </>
                    )}
                  </article>
                );
              })}
            </section>
          ))}
        </div>
      </section>
    </section>
  );
}
