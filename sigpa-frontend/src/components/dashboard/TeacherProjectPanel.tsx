import { FormEvent, useEffect, useState } from 'react';
import { apiFetch, getApiError } from '../../utility/api';

type TeacherMilestone = {
  id: string;
  titulo: string;
  descripcion: string;
  fechaLimite: string;
  porcentajeNota: number;
  estado: string;
  fechaEntrega: string | null;
  permiteEntregaExtemporanea: boolean;
  penalizacionExtemporaneaPct: number;
};

type RubricLevel = {
  id: string;
  nivel: number;
  etiqueta: string;
  descripcion: string;
  puntaje: number;
};

type RubricCriterion = {
  id: string;
  nombre: string;
  descripcion: string;
  pesoPorcentual: number;
  niveles: RubricLevel[];
};

type CriterionDraft = {
  nombre: string;
  descripcion: string;
  pesoPorcentual: string;
  niveles: Array<{ nivel: number; etiqueta: string; descripcion: string; puntaje: number }>;
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

function newCriterion(): CriterionDraft {
  return {
    nombre: '',
    descripcion: '',
    pesoPorcentual: '',
    niveles: [
      { nivel: 1, etiqueta: 'Inicial', descripcion: 'Evidencia insuficiente', puntaje: 0 },
      { nivel: 2, etiqueta: 'En desarrollo', descripcion: 'Cumple parcialmente', puntaje: 60 },
      { nivel: 3, etiqueta: 'Logrado', descripcion: 'Cumple completamente', puntaje: 100 },
    ],
  };
}

export function TeacherProjectPanel({
  projectId,
  milestones,
  evaluatedMilestoneIds,
  onRefresh,
}: {
  projectId: string;
  milestones: TeacherMilestone[];
  evaluatedMilestoneIds: string[];
  onRefresh: () => Promise<void>;
}) {
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [dueAt, setDueAt] = useState('');
  const [weight, setWeight] = useState('');
  const [allowsLate, setAllowsLate] = useState(false);
  const [latePenalty, setLatePenalty] = useState('0');
  const [criteria, setCriteria] = useState<CriterionDraft[]>([{
    ...newCriterion(),
    nombre: 'Criterio 1',
    pesoPorcentual: '100',
  }]);
  const [selectedMilestone, setSelectedMilestone] = useState('');
  const [rubric, setRubric] = useState<RubricCriterion[]>([]);
  const [selectedLevels, setSelectedLevels] = useState<Record<string, string>>({});
  const [criterionObservations, setCriterionObservations] = useState<Record<string, string>>({});
  const [generalObservation, setGeneralObservation] = useState('');
  const [pendingEvaluationIds, setPendingEvaluationIds] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  const evaluableMilestones = milestones.filter((item) => item.fechaEntrega);

  useEffect(() => {
    let active = true;
    setRubric([]);
    setSelectedLevels({});
    if (!selectedMilestone) return () => { active = false; };

    const loadRubric = async () => {
      try {
        const response = await apiFetch(`/api/entregables/${selectedMilestone}/rubrica`);
        if (!response.ok) throw new Error(await getApiError(response));
        const result = await response.json() as RubricCriterion[];
        if (active) setRubric(result);
      } catch (loadError) {
        if (active) setError(loadError instanceof Error ? loadError.message : 'No se pudo cargar la rúbrica.');
      }
    };
    void loadRubric();
    return () => { active = false; };
  }, [selectedMilestone]);

  const createMilestone = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    setMessage(null);
    setSaving(true);
    try {
      const response = await apiFetch('/api/entregables', {
        method: 'POST',
        body: JSON.stringify({
          proyectoId: projectId,
          titulo: title.trim(),
          descripcion: description.trim(),
          fechaLimite: new Date(dueAt).toISOString(),
          porcentajeNota: Number(weight),
          permiteEntregaExtemporanea: allowsLate,
          penalizacionExtemporaneaPct: allowsLate ? Number(latePenalty) : 0,
        }),
      });
      if (!response.ok) throw new Error(await getApiError(response));
      setTitle('');
      setDescription('');
      setDueAt('');
      setWeight('');
      setMessage('Hito agregado al cronograma.');
      await onRefresh();
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : 'No se pudo crear el hito.');
    } finally {
      setSaving(false);
    }
  };

  const createRubric = async (event: FormEvent) => {
    event.preventDefault();
    if (!selectedMilestone) {
      setError('Selecciona un hito para asociar la rúbrica.');
      return;
    }
    const total = criteria.reduce((sum, criterion) => sum + Number(criterion.pesoPorcentual), 0);
    if (total !== 100) {
      setError(`La suma de los criterios es ${total}%. Debe ser exactamente 100%.`);
      return;
    }

    setError(null);
    setMessage(null);
    setSaving(true);
    try {
      const response = await apiFetch(`/api/entregables/${selectedMilestone}/rubrica`, {
        method: 'POST',
        body: JSON.stringify({
          criterios: criteria.map((criterion) => ({
            nombre: criterion.nombre.trim(),
            descripcion: criterion.descripcion.trim(),
            pesoPorcentual: Number(criterion.pesoPorcentual),
            niveles: criterion.niveles,
          })),
        }),
      });
      if (!response.ok) throw new Error(await getApiError(response));
      const rubricResponse = await apiFetch(`/api/entregables/${selectedMilestone}/rubrica`);
      if (!rubricResponse.ok) {
        setMessage('La rúbrica se guardó, pero no se pudo recargar. Cambia de hito y vuelve a seleccionarlo para actualizarla.');
        setError(await getApiError(rubricResponse));
        return;
      }
      setRubric(await rubricResponse.json() as RubricCriterion[]);
      setMessage('Rúbrica creada. La suma de pesos cumple el 100%.');
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : 'No se pudo crear la rúbrica.');
    } finally {
      setSaving(false);
    }
  };

  const submitEvaluation = async (event: FormEvent) => {
    event.preventDefault();
    if (rubric.length === 0 || rubric.some((criterion) => !selectedLevels[criterion.id])) {
      setError('Asigna un nivel de logro a cada criterio antes de guardar la evaluación.');
      return;
    }

    setError(null);
    setMessage(null);
    setSaving(true);
    try {
      let evaluationId = pendingEvaluationIds[selectedMilestone] ?? null;
      let score: number | null = null;
      if (!evaluationId) {
        const response = await apiFetch(`/api/entregables/${selectedMilestone}/evaluaciones`, {
          method: 'POST',
          body: JSON.stringify({
            observacionGeneral: generalObservation.trim(),
            criterios: rubric.map((criterion) => ({
              criterioId: criterion.id,
              nivelId: selectedLevels[criterion.id],
              observacion: criterionObservations[criterion.id] ?? '',
            })),
          }),
        });
        if (!response.ok) throw new Error(await getApiError(response));
        const evaluation = await response.json() as { id: string; puntajeFinal: number };
        const createdEvaluationId = evaluation.id;
        evaluationId = createdEvaluationId;
        score = evaluation.puntajeFinal;
        setPendingEvaluationIds((current) => ({ ...current, [selectedMilestone]: createdEvaluationId }));
      }
      const publishResponse = await apiFetch(`/api/evaluaciones/${evaluationId}/publicar`, { method: 'POST' });
      if (!publishResponse.ok) throw new Error(await getApiError(publishResponse));
      const published = await publishResponse.json() as { puntajeFinal: number };
      setMessage(`Calificación publicada: ${(score ?? published.puntajeFinal).toFixed(2)} / 100.`);
      setPendingEvaluationIds((current) => {
        const pending = { ...current };
        delete pending[selectedMilestone];
        return pending;
      });
      setGeneralObservation('');
      setSelectedLevels({});
      setCriterionObservations({});
      await onRefresh();
    } catch (saveError) {
      const detail = saveError instanceof Error ? saveError.message : 'Error desconocido.';
      setError(pendingEvaluationIds[selectedMilestone]
        ? `El borrador de evaluación se conservó; no se pudo publicar la nota. Puedes reintentar. ${detail}`
        : `No se pudo guardar y publicar la calificación. ${detail}`);
    } finally {
      setSaving(false);
    }
  };

  const updateCriterion = (index: number, updates: Partial<CriterionDraft>) => {
    setCriteria((current) => current.map((criterion, currentIndex) =>
      currentIndex === index ? { ...criterion, ...updates } : criterion));
  };

  return (
    <section aria-label="Herramientas docentes del proyecto" style={{ display: 'grid', gap: '18px', marginTop: '18px', borderTop: '1px solid #334155', paddingTop: '18px' }}>
      {error && <p role="alert" style={{ color: '#fecaca' }}>{error}</p>}
      {message && <p role="status" style={{ color: '#bbf7d0' }}>{message}</p>}

      <section>
        <h4>Diseñador de hitos</h4>
        <form onSubmit={createMilestone} style={{ display: 'grid', gap: '10px' }}>
          <label>Título<input required maxLength={200} value={title} onChange={(event) => setTitle(event.target.value)} style={inputStyle} /></label>
          <label>Descripción<textarea value={description} onChange={(event) => setDescription(event.target.value)} maxLength={4000} style={inputStyle} /></label>
          <label>Fecha límite<input required type="datetime-local" value={dueAt} onChange={(event) => setDueAt(event.target.value)} style={inputStyle} /></label>
          <label>Peso porcentual<input required type="number" min="0" max="100" step="0.01" value={weight} onChange={(event) => setWeight(event.target.value)} style={inputStyle} /></label>
          <label style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
            <input type="checkbox" checked={allowsLate} onChange={(event) => setAllowsLate(event.target.checked)} />
            Permitir entrega extemporánea
          </label>
          {allowsLate && (
            <label>Penalización por entrega tardía (%)<input required type="number" min="0" max="100" step="0.01" value={latePenalty} onChange={(event) => setLatePenalty(event.target.value)} style={inputStyle} /></label>
          )}
          <button disabled={saving} type="submit">{saving ? 'Guardando...' : 'Agregar hito'}</button>
        </form>
      </section>

      <section>
        <h4>Rúbrica y evaluación</h4>
        <label>
          Hito
          <select required value={selectedMilestone} onChange={(event) => setSelectedMilestone(event.target.value)} style={inputStyle}>
            <option value="">Selecciona un hito</option>
            {milestones.map((item) => <option key={item.id} value={item.id}>{item.titulo}</option>)}
          </select>
        </label>

        {selectedMilestone && rubric.length === 0 && (
          <form onSubmit={createRubric} style={{ display: 'grid', gap: '12px', marginTop: '12px' }}>
            {criteria.map((criterion, index) => (
              <fieldset key={index} style={{ display: 'grid', gap: '8px', border: '1px solid #475569', borderRadius: '8px', padding: '12px' }}>
                <legend>Criterio {index + 1}</legend>
                <label>Nombre<input required value={criterion.nombre} onChange={(event) => updateCriterion(index, { nombre: event.target.value })} style={inputStyle} /></label>
                <label>Descripción<input value={criterion.descripcion} onChange={(event) => updateCriterion(index, { descripcion: event.target.value })} style={inputStyle} /></label>
                <label>Peso (%)<input required type="number" min="0.01" max="100" step="0.01" value={criterion.pesoPorcentual} onChange={(event) => updateCriterion(index, { pesoPorcentual: event.target.value })} style={inputStyle} /></label>
                <div style={{ display: 'grid', gap: '6px' }}>
                  {criterion.niveles.map((level, levelIndex) => (
                    <div key={level.nivel} style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 90px', gap: '6px' }}>
                      <label>{`Nivel ${level.nivel}`}<input value={level.etiqueta} onChange={(event) => {
                        const levels = criterion.niveles.map((item, position) => position === levelIndex ? { ...item, etiqueta: event.target.value } : item);
                        updateCriterion(index, { niveles: levels });
                      }} style={inputStyle} /></label>
                      <label>Descripción<input value={level.descripcion} onChange={(event) => {
                        const levels = criterion.niveles.map((item, position) => position === levelIndex ? { ...item, descripcion: event.target.value } : item);
                        updateCriterion(index, { niveles: levels });
                      }} style={inputStyle} /></label>
                      <label>Puntos<input type="number" min="0" max="100" step="0.01" value={level.puntaje} onChange={(event) => {
                        const levels = criterion.niveles.map((item, position) => position === levelIndex ? { ...item, puntaje: Number(event.target.value) } : item);
                        updateCriterion(index, { niveles: levels });
                      }} style={inputStyle} /></label>
                    </div>
                  ))}
                </div>
              </fieldset>
            ))}
            <div aria-live="polite">Total de ponderaciones: {criteria.reduce((sum, criterion) => sum + Number(criterion.pesoPorcentual), 0)}%</div>
            <button type="button" onClick={() => setCriteria((current) => [...current, newCriterion()])}>Agregar criterio</button>
            <button disabled={saving || criteria.reduce((sum, criterion) => sum + Number(criterion.pesoPorcentual), 0) !== 100} type="submit">
              {saving ? 'Guardando...' : 'Crear rúbrica'}
            </button>
          </form>
        )}

        {selectedMilestone && rubric.length > 0 && evaluatedMilestoneIds.includes(selectedMilestone) && (
          <p role="status">Este hito ya tiene una evaluación registrada.</p>
        )}
        {selectedMilestone && rubric.length > 0 && !evaluatedMilestoneIds.includes(selectedMilestone) && evaluableMilestones.some((item) => item.id === selectedMilestone) && (
          <form onSubmit={submitEvaluation} style={{ display: 'grid', gap: '12px', marginTop: '12px' }}>
            {rubric.map((criterion) => (
              <fieldset key={criterion.id} style={{ border: '1px solid #475569', borderRadius: '8px', padding: '12px' }}>
                <legend>{criterion.nombre} — {criterion.pesoPorcentual}%</legend>
                <label>
                  Nivel de logro
                  <select required value={selectedLevels[criterion.id] ?? ''} onChange={(event) => setSelectedLevels((current) => ({ ...current, [criterion.id]: event.target.value }))} style={inputStyle}>
                    <option value="">Selecciona nivel</option>
                    {criterion.niveles.map((level) => <option key={level.id} value={level.id}>{level.etiqueta} — {level.puntaje} puntos</option>)}
                  </select>
                </label>
                <label>Observación<input value={criterionObservations[criterion.id] ?? ''} onChange={(event) => setCriterionObservations((current) => ({ ...current, [criterion.id]: event.target.value }))} style={inputStyle} /></label>
              </fieldset>
            ))}
            <label>Observación general<textarea required value={generalObservation} onChange={(event) => setGeneralObservation(event.target.value)} style={inputStyle} rows={3} /></label>
            <button disabled={saving} type="submit">
              {saving ? 'Publicando...' : pendingEvaluationIds[selectedMilestone] ? 'Reintentar publicación' : 'Calificar y publicar nota'}
            </button>
          </form>
        )}
        {selectedMilestone && rubric.length > 0 && !evaluableMilestones.some((item) => item.id === selectedMilestone) && (
          <p role="status" style={{ color: '#94a3b8' }}>La rúbrica está configurada. La evaluación se habilitará cuando el equipo entregue este hito.</p>
        )}
      </section>
    </section>
  );
}
