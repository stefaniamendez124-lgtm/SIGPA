-- Migración incremental: ejecutar después de schema.sql y extension_academica.sql.
-- Conserva cursos y matriculas como tablas canónicas para no romper RN-02.

alter table public.usuarios
  add column if not exists tipo_documento text,
  add column if not exists numero_identificacion text,
  add column if not exists codigo_identificacion text,
  add column if not exists semestre integer,
  add column if not exists programa_academico text,
  add column if not exists departamento_academico text,
  add column if not exists activo boolean not null default true,
  add column if not exists debe_cambiar_contrasena boolean not null default false;

alter table public.cursos
  add column if not exists semestre integer,
  add column if not exists programa_academico text,
  add column if not exists periodo_academico text;

create unique index if not exists usuarios_correo_institucional_lower_uidx
  on public.usuarios (lower(correo_institucional));

create unique index if not exists usuarios_numero_identificacion_uidx
  on public.usuarios (numero_identificacion)
  where numero_identificacion is not null;

create unique index if not exists usuarios_codigo_identificacion_uidx
  on public.usuarios (codigo_identificacion)
  where codigo_identificacion is not null;

create index if not exists usuarios_rol_activo_idx
  on public.usuarios (rol, activo);

create table if not exists public.docentes_cursos (
  id uuid primary key default gen_random_uuid(),
  usuario_id uuid not null references public.usuarios(id) on delete cascade,
  curso_id uuid not null references public.cursos(id) on delete cascade,
  rol_docente text not null default 'Titular'
    check (rol_docente in ('Titular', 'Apoyo')),
  asignado_at timestamptz not null default now(),
  unique (usuario_id, curso_id)
);

create index if not exists docentes_cursos_usuario_idx
  on public.docentes_cursos (usuario_id, curso_id);
create index if not exists docentes_cursos_curso_idx
  on public.docentes_cursos (curso_id, usuario_id);

insert into public.docentes_cursos (usuario_id, curso_id, rol_docente)
select docente_id, id, 'Titular'
from public.cursos
where docente_id is not null
on conflict (usuario_id, curso_id) do nothing;

-- Vistas de compatibilidad con los nombres del modelo de administración.
create or replace view public.estudiantes_cursos as
select id, estudiante_id as usuario_id, curso_id, creada_at as fecha_matricula, estado
from public.matriculas;

create or replace view public.cursos_asignaturas as
select id, codigo as codigo_materia, nombre, semestre, programa_academico, periodo_academico, activo
from public.cursos;

alter table public.docentes_cursos enable row level security;
revoke all on public.docentes_cursos from public, anon, authenticated;
revoke all on public.estudiantes_cursos, public.cursos_asignaturas from public, anon, authenticated;

comment on column public.usuarios.debe_cambiar_contrasena is
  'Indica que Supabase Auth debe forzar el flujo de cambio de contraseña temporal al primer acceso.';
comment on view public.estudiantes_cursos is
  'Vista de compatibilidad: la tabla matriculas es la fuente canónica usada por RN-02.';
comment on view public.cursos_asignaturas is
  'Vista de compatibilidad: cursos es la tabla canónica de asignaturas de SIGPA.';
