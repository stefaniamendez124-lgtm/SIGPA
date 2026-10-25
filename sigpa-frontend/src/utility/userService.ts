import { apiFetch, getApiError } from './api';

export type AcademicCourse = {
  id: string;
  codigo: string;
  nombre: string;
  semestre: number | null;
  programaAcademico: string | null;
  periodoAcademico: string | null;
  activo: boolean;
};

export type ManagedUser = {
  id: string;
  nombreCompleto: string;
  correoInstitucional: string;
  rol: 'Estudiante' | 'Profesor';
  tipoDocumento: string;
  numeroIdentificacion: string;
  codigoIdentificacion: string | null;
  semestre: number | null;
  programaAcademico: string | null;
  departamentoAcademico: string | null;
  activo: boolean;
  debeCambiarContrasena: boolean;
  cursos: AcademicCourse[];
};

export type UserFormData = {
  nombreCompleto: string;
  tipoDocumento: string;
  numeroIdentificacion: string;
  correoInstitucional: string;
  rol: 'Estudiante' | 'Profesor';
  contrasena: string;
  codigoIdentificacion: string;
  semestre: number | null;
  programaAcademico: string;
  departamentoAcademico: string;
  cursoIds: string[];
  enviarCorreoBienvenida: boolean;
};

export type ManagedUserPage = {
  items: ManagedUser[];
  pagina: number;
  tamanoPagina: number;
  total: number;
};

export type UserUploadRow = {
  fila: number;
  usuario: UserFormData;
  errores: string[];
};

export async function requestJson<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await apiFetch(path, init);
  if (!response.ok) throw new Error(await getApiError(response));
  if (response.status === 204) return undefined as T;
  return await response.json() as T;
}

export const userService = {
  list(params: URLSearchParams) {
    const query = params.toString();
    return requestJson<ManagedUserPage>(`/api/v1/usuarios${query ? `?${query}` : ''}`);
  },

  courses() {
    return requestJson<AcademicCourse[]>('/api/v1/usuarios/cursos');
  },

  create(user: UserFormData) {
    return requestJson<{ usuario: ManagedUser; correoBienvenidaEnviado: boolean; advertencia: string | null }>(
      '/api/v1/usuarios',
      { method: 'POST', body: JSON.stringify(user) },
    );
  },

  update(userId: string, user: UserFormData & { activo: boolean }) {
    return requestJson<ManagedUser>(`/api/v1/usuarios/${userId}`, {
      method: 'PUT',
      body: JSON.stringify(user),
    });
  },

  assignCourses(userId: string, courseIds: string[]) {
    return requestJson<ManagedUser>(`/api/v1/usuarios/${userId}/asignar-cursos`, {
      method: 'POST',
      body: JSON.stringify({ cursoIds: courseIds }),
    });
  },

  validateUpload(file: File) {
    const formData = new FormData();
    formData.set('archivo', file);
    return requestJson<UserUploadRow[]>('/api/v1/usuarios/carga-masiva/validar', {
      method: 'POST',
      body: formData,
    });
  },

  importRows(rows: UserUploadRow[]) {
    return requestJson<{
      creados: number;
      fallidos: number;
      errores: Array<{ fila: number; correo: string; error: string }>;
      correosPendientes: Array<{ fila: number; correo: string; error: string }>;
    }>(
      '/api/v1/usuarios/carga-masiva',
      { method: 'POST', body: JSON.stringify({ filas: rows }) },
    );
  },

  async downloadTemplate() {
    const response = await apiFetch('/api/v1/usuarios/plantilla.csv');
    if (!response.ok) throw new Error(await getApiError(response));
    return await response.blob();
  },
};
