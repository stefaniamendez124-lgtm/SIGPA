alter table public.proyectos
  drop constraint if exists proyectos_estado_check;

alter table public.proyectos
  add constraint proyectos_estado_check
  check (
    estado in (
      'Pendiente',
      'En revisión',
      'Aprobado',
      'Rechazado',
      'Entregado',
      'En progreso'
    )
  );

notify pgrst, 'reload schema';
