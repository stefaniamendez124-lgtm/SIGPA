alter table public.proyectos
  add column if not exists curso text not null default 'Ingeniería de requisitos',
  add column if not exists progreso integer not null default 15,
  add column if not exists fecha_entrega date,
  add column if not exists created_at timestamptz not null default now(),
  add column if not exists updated_at timestamptz not null default now();

update public.proyectos
set fecha_entrega = fecha_fin::date
where fecha_entrega is null;

notify pgrst, 'reload schema';
