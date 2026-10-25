import { ChangeEvent, FormEvent, ReactNode, useEffect, useMemo, useState } from 'react';
import {
  AcademicCourse,
  ManagedUser,
  UserFormData,
  UserUploadRow,
  userService,
} from '../../utility/userService';

const pageStyle = {
  background: 'rgba(15, 23, 42, 0.8)',
  border: '1px solid rgba(148, 163, 184, 0.2)',
  borderRadius: '16px',
  padding: '18px',
};
const inputStyle = {
  boxSizing: 'border-box' as const,
  width: '100%',
  border: '1px solid #475569',
  borderRadius: '8px',
  padding: '9px',
  background: '#020817',
  color: '#f8fafc',
};
const pageSize = 20;

type ModalMode = 'create' | 'edit' | 'courses' | null;

function emptyForm(): UserFormData {
  return {
    nombreCompleto: '',
    tipoDocumento: 'CC',
    numeroIdentificacion: '',
    correoInstitucional: '',
    rol: 'Estudiante',
    contrasena: '',
    codigoIdentificacion: '',
    semestre: null,
    programaAcademico: '',
    departamentoAcademico: '',
    cursoIds: [],
    enviarCorreoBienvenida: true,
  };
}

function userToForm(user: ManagedUser): UserFormData {
  return {
    nombreCompleto: user.nombreCompleto,
    tipoDocumento: user.tipoDocumento,
    numeroIdentificacion: user.numeroIdentificacion,
    correoInstitucional: user.correoInstitucional,
    rol: user.rol,
    contrasena: '',
    codigoIdentificacion: user.codigoIdentificacion ?? '',
    semestre: user.semestre,
    programaAcademico: user.programaAcademico ?? '',
    departamentoAcademico: user.departamentoAcademico ?? '',
    cursoIds: user.cursos.map((course) => course.id),
    enviarCorreoBienvenida: false,
  };
}

export function UserManagement() {
  const [users, setUsers] = useState<ManagedUser[]>([]);
  const [courses, setCourses] = useState<AcademicCourse[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [role, setRole] = useState('Todos');
  const [program, setProgram] = useState('');
  const [courseId, setCourseId] = useState('');
  const [activeFilter, setActiveFilter] = useState('Todos');
  const [search, setSearch] = useState('');
  const [modal, setModal] = useState<ModalMode>(null);
  const [selectedUser, setSelectedUser] = useState<ManagedUser | null>(null);
  const [form, setForm] = useState<UserFormData>(emptyForm);
  const [saving, setSaving] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [uploadRows, setUploadRows] = useState<UserUploadRow[] | null>(null);
  const [uploading, setUploading] = useState(false);
  const [bulkResult, setBulkResult] = useState<string | null>(null);
  const [bulkSendWelcome, setBulkSendWelcome] = useState(true);

  const programs = useMemo(
    () => Array.from(new Set(courses.map((course) => course.programaAcademico).filter((value): value is string => Boolean(value)))).sort(),
    [courses],
  );
  const filteredCourses = useMemo(
    () => courses.filter((course) => !program || course.programaAcademico === program),
    [courses, program],
  );
  const formCourses = useMemo(
    () => courses.filter((course) =>
      (!form.programaAcademico || !course.programaAcademico || course.programaAcademico === form.programaAcademico) &&
      (!form.semestre || !course.semestre || course.semestre === form.semestre)),
    [courses, form.programaAcademico, form.semestre],
  );
  const validRows = uploadRows?.filter((row) => row.errores.length === 0) ?? [];
  const errorRows = uploadRows?.filter((row) => row.errores.length > 0) ?? [];

  const refreshCourses = async () => {
    try {
      setCourses(await userService.courses());
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'No se pudieron cargar las asignaturas activas.');
    }
  };

  useEffect(() => {
    void refreshCourses();
  }, []);

  useEffect(() => {
    let current = true;
    setLoading(true);
    const timeout = window.setTimeout(() => {
      const query = new URLSearchParams({
        pagina: String(page),
        tamanoPagina: String(pageSize),
      });
      if (role !== 'Todos') query.set('rol', role);
      if (program) query.set('programa', program);
      if (courseId) query.set('cursoId', courseId);
      if (activeFilter !== 'Todos') query.set('activo', activeFilter);
      if (search.trim()) query.set('busqueda', search.trim());

      void userService.list(query)
        .then((result) => {
          if (!current) return;
          setUsers(result.items);
          setTotal(result.total);
          setError(null);
        })
        .catch((loadError: unknown) => {
          if (current) setError(loadError instanceof Error ? loadError.message : 'No se pudieron cargar los usuarios.');
        })
        .finally(() => {
          if (current) setLoading(false);
        });
    }, search ? 250 : 0);

    return () => {
      current = false;
      window.clearTimeout(timeout);
    };
  }, [page, role, program, courseId, activeFilter, search]);

  const resetFilters = () => {
    setPage(1);
    setRole('Todos');
    setProgram('');
    setCourseId('');
    setActiveFilter('Todos');
    setSearch('');
  };

  const openCreate = () => {
    setSelectedUser(null);
    setForm(emptyForm());
    setModal('create');
    setError(null);
    setNotice(null);
  };

  const openEdit = (user: ManagedUser) => {
    setSelectedUser(user);
    setForm(userToForm(user));
    setModal('edit');
    setError(null);
    setNotice(null);
  };

  const openCourseEditor = (user: ManagedUser) => {
    setSelectedUser(user);
    setForm(userToForm(user));
    setModal('courses');
    setError(null);
    setNotice(null);
  };

  const handleFormSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    setNotice(null);
    setSaving(true);
    try {
      if (modal === 'create') {
        const result = await userService.create(form);
        setNotice(result.advertencia ??
          (result.correoBienvenidaEnviado
            ? 'Usuario creado; el correo de bienvenida fue enviado.'
            : 'Usuario creado. No se solicitó el envío del correo de bienvenida.'));
      } else if (modal === 'edit' && selectedUser) {
        await userService.update(selectedUser.id, { ...form, activo: selectedUser.activo });
        setNotice('Datos del usuario actualizados.');
      } else if (modal === 'courses' && selectedUser) {
        await userService.assignCourses(selectedUser.id, form.cursoIds);
        setNotice('Asignaturas actualizadas.');
      }
      setModal(null);
      await refreshUsers();
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : 'No se pudo guardar el usuario.');
    } finally {
      setSaving(false);
    }
  };

  const refreshUsers = async () => {
    const query = new URLSearchParams({ pagina: String(page), tamanoPagina: String(pageSize) });
    if (role !== 'Todos') query.set('rol', role);
    if (program) query.set('programa', program);
    if (courseId) query.set('cursoId', courseId);
    if (activeFilter !== 'Todos') query.set('activo', activeFilter);
    if (search.trim()) query.set('busqueda', search.trim());
    const result = await userService.list(query);
    setUsers(result.items);
    setTotal(result.total);
  };

  const toggleUser = async (user: ManagedUser) => {
    setError(null);
    setNotice(null);
    try {
      const values = userToForm(user);
      await userService.update(user.id, { ...values, activo: !user.activo });
      setNotice(`Cuenta ${user.activo ? 'deshabilitada' : 'activada'}.`);
      await refreshUsers();
    } catch (updateError) {
      setError(updateError instanceof Error ? updateError.message : 'No se pudo cambiar el estado.');
    }
  };

  const processFile = async (file: File | undefined) => {
    if (!file) return;
    if (!/\.(csv|xlsx)$/i.test(file.name)) {
      setError('Selecciona un archivo CSV o Excel .xlsx.');
      return;
    }
    if (file.size > 10 * 1024 * 1024) {
      setError('El archivo supera el límite de 10 MB.');
      return;
    }
    setError(null);
    setNotice(null);
    setBulkResult(null);
    setUploading(true);
    try {
      setUploadRows(await userService.validateUpload(file));
    } catch (uploadError) {
      setError(uploadError instanceof Error ? uploadError.message : 'No se pudo validar el archivo.');
    } finally {
      setUploading(false);
    }
  };

  const validateFile = (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = '';
    void processFile(file);
  };

  const confirmBulkImport = async () => {
    if (!uploadRows || validRows.length === 0) return;
    setUploading(true);
    setError(null);
    try {
      const rows = uploadRows.map((row) => ({
        ...row,
        usuario: { ...row.usuario, enviarCorreoBienvenida: bulkSendWelcome && row.errores.length === 0 },
      }));
      const result = await userService.importRows(rows);
      const emailNotice = result.correosPendientes.length > 0
        ? ` Correos de bienvenida pendientes: ${result.correosPendientes.length}.`
        : '';
      setBulkResult(`Cuentas creadas: ${result.creados}. Filas con error: ${result.fallidos}.${emailNotice}`);
      if (result.correosPendientes.length > 0)
        setError(result.correosPendientes.map((item) => `Fila ${item.fila} (${item.correo}): ${item.error}`).join(' '));
      if (result.errores.length > 0) {
        setUploadRows((rows) => rows?.map((row) => {
          const issue = result.errores.find((item) => item.fila === row.fila);
          return issue ? { ...row, errores: [...row.errores, issue.error] } : row;
        }) ?? null);
      } else {
        setUploadRows(null);
      }
      await refreshUsers();
    } catch (importError) {
      setError(importError instanceof Error ? importError.message : 'No se pudo completar la carga masiva.');
    } finally {
      setUploading(false);
    }
  };

  const downloadTemplate = async () => {
    try {
      const blob = await userService.downloadTemplate();
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = 'plantilla-usuarios-sigpa.csv';
      link.click();
      URL.revokeObjectURL(url);
    } catch (downloadError) {
      setError(downloadError instanceof Error ? downloadError.message : 'No se pudo descargar la plantilla.');
    }
  };

  const toggleCourse = (id: string) => {
    setForm((current) => ({
      ...current,
      cursoIds: current.cursoIds.includes(id)
        ? current.cursoIds.filter((courseIdValue) => courseIdValue !== id)
        : [...current.cursoIds, id],
    }));
  };

  return (
    <section aria-labelledby="user-management-title" style={{ display: 'grid', gap: '16px' }}>
      <header style={{ ...pageStyle, display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px', flexWrap: 'wrap' }}>
        <div>
          <h2 id="user-management-title" style={{ margin: 0 }}>Gestión centralizada de usuarios</h2>
          <p style={{ color: '#94a3b8', marginBottom: 0 }}>Solo el Coordinador Académico puede crear, editar, deshabilitar o reasignar cursos.</p>
        </div>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px' }}>
          <button type="button" onClick={openCreate}>Registrar usuario</button>
          <label style={{ cursor: 'pointer' }}>
            <span style={{ display: 'inline-block', padding: '8px 12px', border: '1px solid #475569', borderRadius: '8px' }}>
              {uploading ? 'Procesando...' : 'Carga masiva (CSV/XLSX)'}
            </span>
            <input type="file" accept=".csv,.xlsx" onChange={(event) => void validateFile(event)} disabled={uploading} style={{ display: 'none' }} />
          </label>
          <button type="button" onClick={() => void downloadTemplate()}>Descargar plantilla CSV</button>
        </div>
      </header>

      <div
        onDragOver={(event) => event.preventDefault()}
        onDrop={(event) => {
          event.preventDefault();
          void processFile(event.dataTransfer.files[0]);
        }}
        style={{ ...pageStyle, borderStyle: 'dashed', textAlign: 'center', color: '#cbd5e1' }}
      >
        Arrastra aquí un archivo CSV o XLSX, o selecciónalo con el botón de carga masiva.
      </div>

      {error && <p role="alert" style={{ ...pageStyle, color: '#fecaca' }}>{error}</p>}
      {notice && <p role="status" style={{ ...pageStyle, color: '#bbf7d0' }}>{notice}</p>}

      {uploadRows && (
        <section aria-labelledby="bulk-preview-title" style={pageStyle}>
          <h3 id="bulk-preview-title">Vista previa de carga</h3>
          <p>{validRows.length} filas válidas · {errorRows.length} filas con errores</p>
          <label style={{ display: 'flex', gap: '8px', alignItems: 'center', marginBottom: '12px' }}>
            <input type="checkbox" checked={bulkSendWelcome} onChange={(event) => setBulkSendWelcome(event.target.checked)} />
            Enviar correo de bienvenida a las cuentas importadas
          </label>
          {errorRows.length > 0 && (
            <ul style={{ color: '#fecaca' }}>
              {errorRows.map((row) => <li key={row.fila}>Fila {row.fila} ({row.usuario.correoInstitucional}): {row.errores.join(' ')}</li>)}
            </ul>
          )}
          {bulkResult && <p role="status">{bulkResult}</p>}
          <div style={{ display: 'flex', gap: '8px' }}>
            <button type="button" disabled={uploading || validRows.length === 0} onClick={() => void confirmBulkImport()}>
              {uploading ? 'Importando...' : `Confirmar ${validRows.length} registros válidos`}
            </button>
            <button type="button" disabled={uploading} onClick={() => { setUploadRows(null); setBulkResult(null); }}>Cancelar carga</button>
          </div>
        </section>
      )}

      <section aria-label="Filtros de usuarios" style={{ ...pageStyle, display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(170px, 1fr))', gap: '10px' }}>
        <label>Buscar
          <input value={search} onChange={(event) => { setPage(1); setSearch(event.target.value); }} placeholder="Nombre, código, documento o correo" style={inputStyle} />
        </label>
        <label>Rol
          <select value={role} onChange={(event) => { setPage(1); setRole(event.target.value); }} style={inputStyle}>
            <option>Todos</option><option>Estudiante</option><option>Profesor</option>
          </select>
        </label>
        <label>Programa
          <select value={program} onChange={(event) => { setPage(1); setProgram(event.target.value); setCourseId(''); }} style={inputStyle}>
            <option value="">Todos los programas</option>
            {programs.map((item) => <option key={item}>{item}</option>)}
          </select>
        </label>
        <label>Asignatura
          <select value={courseId} onChange={(event) => { setPage(1); setCourseId(event.target.value); }} style={inputStyle}>
            <option value="">Todas las asignaturas</option>
            {filteredCourses.map((course) => <option key={course.id} value={course.id}>{course.nombre} ({course.codigo})</option>)}
          </select>
        </label>
        <label>Estado
          <select value={activeFilter} onChange={(event) => { setPage(1); setActiveFilter(event.target.value); }} style={inputStyle}>
            <option>Todos</option><option value="true">Activo</option><option value="false">Inactivo</option>
          </select>
        </label>
        <button type="button" onClick={resetFilters} style={{ alignSelf: 'end' }}>Limpiar filtros</button>
      </section>

      <div style={{ ...pageStyle, overflowX: 'auto' }}>
        {loading ? <p role="status">Cargando usuarios...</p> : (
          <table style={{ width: '100%', borderCollapse: 'collapse', minWidth: '900px' }}>
            <thead><tr>
              {['Nombre / correo', 'Rol / código', 'Identificación', 'Programa / departamento', 'Cursos', 'Estado', 'Acciones'].map((title) => (
                <th key={title} scope="col" style={{ padding: '10px', textAlign: 'left', borderBottom: '1px solid #475569' }}>{title}</th>
              ))}
            </tr></thead>
            <tbody>
              {users.length === 0 ? (
                <tr><td colSpan={7} style={{ padding: '18px', textAlign: 'center', color: '#94a3b8' }}>No hay usuarios que coincidan con los filtros.</td></tr>
              ) : users.map((user) => (
                <tr key={user.id}>
                  <td style={{ padding: '10px', borderBottom: '1px solid #334155' }}>{user.nombreCompleto}<br /><small>{user.correoInstitucional}</small></td>
                  <td style={{ padding: '10px', borderBottom: '1px solid #334155' }}>{user.rol}<br /><small>{user.codigoIdentificacion}</small></td>
                  <td style={{ padding: '10px', borderBottom: '1px solid #334155' }}>{user.tipoDocumento} {user.numeroIdentificacion}</td>
                  <td style={{ padding: '10px', borderBottom: '1px solid #334155' }}>{user.programaAcademico ?? user.departamentoAcademico ?? '—'}{user.semestre ? ` · Sem. ${user.semestre}` : ''}</td>
                  <td style={{ padding: '10px', borderBottom: '1px solid #334155' }}>{user.cursos.map((course) => course.codigo).join(', ') || '—'}</td>
                  <td style={{ padding: '10px', borderBottom: '1px solid #334155' }}>{user.activo ? 'Activo' : 'Inactivo'}</td>
                  <td style={{ padding: '10px', borderBottom: '1px solid #334155' }}>
                    <div style={{ display: 'flex', gap: '6px', flexWrap: 'wrap' }}>
                      <button type="button" onClick={() => openEdit(user)}>Editar</button>
                      <button type="button" onClick={() => void toggleUser(user)}>{user.activo ? 'Deshabilitar' : 'Activar'}</button>
                      <button type="button" onClick={() => openCourseEditor(user)}>Reasignar cursos</button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        <footer style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: '14px' }}>
          <span>{total} usuarios · Página {page} de {Math.max(1, Math.ceil(total / pageSize))}</span>
          <span style={{ display: 'flex', gap: '8px' }}>
            <button type="button" disabled={page <= 1 || loading} onClick={() => setPage((current) => current - 1)}>Anterior</button>
            <button type="button" disabled={page >= Math.ceil(total / pageSize) || loading} onClick={() => setPage((current) => current + 1)}>Siguiente</button>
          </span>
        </footer>
      </div>

      {modal && (
        <div role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget && !saving) setModal(null); }} style={{ position: 'fixed', inset: 0, zIndex: 50, display: 'grid', placeItems: 'center', padding: '16px', background: 'rgba(2, 6, 23, .82)' }}>
          <section role="dialog" aria-modal="true" aria-labelledby="user-form-title" style={{ ...pageStyle, width: 'min(760px, 100%)', maxHeight: '90vh', overflowY: 'auto', background: '#0f172a' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', gap: '12px' }}>
              <h2 id="user-form-title" style={{ marginTop: 0 }}>
                {modal === 'create' ? 'Registrar usuario' : modal === 'edit' ? 'Editar usuario' : 'Reasignar cursos'}
              </h2>
              <button type="button" onClick={() => setModal(null)} disabled={saving} aria-label="Cerrar">Cerrar</button>
            </div>
            {modal === 'courses' ? (
              <form onSubmit={handleFormSubmit} style={{ display: 'grid', gap: '14px' }}>
                <p>{selectedUser?.nombreCompleto} · {selectedUser?.rol}</p>
                <CourseSelector courses={courses} selected={form.cursoIds} onToggle={toggleCourse} />
                {error && <p role="alert" style={{ color: '#fecaca' }}>{error}</p>}
                <button type="submit" disabled={saving || form.cursoIds.length === 0}>{saving ? 'Guardando...' : 'Guardar cursos'}</button>
              </form>
            ) : (
              <form onSubmit={handleFormSubmit} style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(230px, 1fr))', gap: '12px' }}>
                <Field label="Nombre completo"><input required minLength={2} maxLength={200} value={form.nombreCompleto} onChange={(event) => setForm({ ...form, nombreCompleto: event.target.value })} style={inputStyle} /></Field>
                <Field label="Tipo de documento">
                  <select required value={form.tipoDocumento} onChange={(event) => setForm({ ...form, tipoDocumento: event.target.value })} style={inputStyle}>
                    <option value="CC">Cédula de ciudadanía</option><option value="CE">Cédula de extranjería</option>
                    <option value="TI">Tarjeta de identidad</option><option value="PA">Pasaporte</option><option value="OTRO">Otro</option>
                  </select>
                </Field>
                <Field label="Número de identificación"><input required minLength={4} maxLength={40} value={form.numeroIdentificacion} onChange={(event) => setForm({ ...form, numeroIdentificacion: event.target.value })} style={inputStyle} /></Field>
                <Field label="Correo institucional"><input required type="email" pattern="^[^@\s]+@[^@\s]+\.[^@\s]+$" maxLength={254} value={form.correoInstitucional} onChange={(event) => setForm({ ...form, correoInstitucional: event.target.value })} style={inputStyle} /></Field>
                <Field label="Rol">
                  <select disabled={modal === 'edit'} value={form.rol} onChange={(event) => setForm({ ...form, rol: event.target.value as UserFormData['rol'], cursoIds: [] })} style={inputStyle}>
                    <option value="Estudiante">Estudiante</option><option value="Profesor">Profesor</option>
                  </select>
                </Field>
                {modal === 'create' && (
                  <Field label="Contraseña inicial">
                    <input
                      required
                      type="password"
                      autoComplete="new-password"
                      minLength={12}
                      maxLength={128}
                      pattern="(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).{12,128}"
                      title="Usa al menos 12 caracteres e incluye mayúscula, minúscula, número y símbolo."
                      value={form.contrasena}
                      onChange={(event) => setForm({ ...form, contrasena: event.target.value })}
                      style={inputStyle}
                    />
                  </Field>
                )}
                <Field label={form.rol === 'Estudiante' ? 'Código estudiantil' : 'ID docente'}>
                  <input required maxLength={40} value={form.codigoIdentificacion} onChange={(event) => setForm({ ...form, codigoIdentificacion: event.target.value })} style={inputStyle} />
                </Field>
                {form.rol === 'Estudiante' ? (
                  <>
                    <Field label="Semestre"><input required type="number" min={1} max={20} value={form.semestre ?? ''} onChange={(event) => setForm({ ...form, semestre: event.target.value ? Number(event.target.value) : null, cursoIds: [] })} style={inputStyle} /></Field>
                    <Field label="Programa académico">
                      <input required list="program-options" maxLength={160} value={form.programaAcademico} onChange={(event) => setForm({ ...form, programaAcademico: event.target.value, cursoIds: [] })} style={inputStyle} />
                      <datalist id="program-options">{programs.map((item) => <option key={item} value={item} />)}</datalist>
                    </Field>
                  </>
                ) : (
                  <Field label="Departamento académico"><input required maxLength={160} value={form.departamentoAcademico} onChange={(event) => setForm({ ...form, departamentoAcademico: event.target.value })} style={inputStyle} /></Field>
                )}
                {modal === 'create' && (
                  <div style={{ gridColumn: '1 / -1' }}>
                    <CourseSelector courses={formCourses} selected={form.cursoIds} onToggle={toggleCourse} />
                  </div>
                )}
                {modal === 'create' && (
                  <label style={{ gridColumn: '1 / -1', display: 'flex', gap: '8px', alignItems: 'center' }}>
                    <input type="checkbox" checked={form.enviarCorreoBienvenida} onChange={(event) => setForm({ ...form, enviarCorreoBienvenida: event.target.checked })} />
                    Enviar por correo las credenciales iniciales
                  </label>
                )}
                {error && <p role="alert" style={{ gridColumn: '1 / -1', color: '#fecaca' }}>{error}</p>}
                <button type="submit" disabled={saving || (modal === 'create' && form.cursoIds.length === 0)} style={{ gridColumn: '1 / -1' }}>
                  {saving ? 'Guardando...' : modal === 'create' ? 'Crear cuenta' : 'Guardar cambios'}
                </button>
              </form>
            )}
          </section>
        </div>
      )}
    </section>
  );
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return <label style={{ display: 'grid', gap: '5px' }}>{label}{children}</label>;
}

function CourseSelector({
  courses,
  selected,
  onToggle,
}: {
  courses: AcademicCourse[];
  selected: string[];
  onToggle: (id: string) => void;
}) {
  return (
    <fieldset style={{ border: '1px solid #475569', borderRadius: '8px', padding: '12px', maxHeight: '220px', overflowY: 'auto' }}>
      <legend>Asignaturas activas (selecciona al menos una)</legend>
      {courses.length === 0 ? <p>No hay cursos activos que coincidan con el programa y semestre seleccionados.</p> : (
        courses.map((course) => (
          <label key={course.id} style={{ display: 'flex', gap: '8px', padding: '5px 0' }}>
            <input type="checkbox" checked={selected.includes(course.id)} onChange={() => onToggle(course.id)} />
            <span>{course.nombre} ({course.codigo}){course.periodoAcademico ? ` · ${course.periodoAcademico}` : ''}</span>
          </label>
        ))
      )}
    </fieldset>
  );
}
