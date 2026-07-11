<template>
  <Dialog v-model:open="internalOpen">
    <DialogContent class="sm:max-w-2xl">
      <DialogHeader>
        <DialogTitle>Уточнение совместимости</DialogTitle>
        <DialogDescription>
          Ответьте на вопросы про смысл данных. Это помогает создать только осмысленные связи.
          <span v-if="report"> Score: {{ Math.min(100, Math.round((report.compatibilityScore || 0) * 100)) }}%</span>
        </DialogDescription>
      </DialogHeader>

      <div v-if="loading" class="py-8 text-center text-muted-foreground">
        Загружаем вопросы…
      </div>

      <div v-else-if="questions.length" class="space-y-6 max-h-[55vh] overflow-auto py-2 pr-1">
        <div v-for="(q, idx) in questions" :key="q.id || idx" class="border rounded-lg p-4">
          <div v-if="q.adapterFieldName || q.AdapterFieldName" class="text-xs text-muted-foreground mb-1">
            Поле: {{ q.adapterFieldName || q.AdapterFieldName }}
          </div>
          <div class="font-medium text-sm mb-3">{{ q.question || q.Question }}</div>

          <div class="space-y-1.5">
            <template v-if="q.candidates && q.candidates.length">
              <label
                v-for="(cand, cidx) in q.candidates"
                :key="cidx"
                class="flex items-start gap-2 text-sm cursor-pointer hover:bg-accent/50 p-1 rounded"
              >
                <input
                  type="radio"
                  :name="`q-${q.id || idx}`"
                  :value="getCandidateValue(cand)"
                  v-model="answers[q.adapterFieldKey || q.AdapterFieldKey]"
                  class="mt-1"
                />
                <div class="flex-1">
                  <div>{{ cand.sourceAttributeName || cand.SourceAttributeName || cand.sourceField || cand.SourceField || 'Вариант' }}</div>
                  <div v-if="cand.score" class="text-[10px] text-muted-foreground">
                    {{ Math.round((cand.score || 0) * 100) }}% совпадение
                  </div>
                </div>
              </label>
            </template>

            <label class="flex items-start gap-2 text-sm cursor-pointer hover:bg-accent/50 p-1 rounded">
              <input
                type="radio"
                :name="`q-${q.id || idx}`"
                value="__other__"
                v-model="answers[q.adapterFieldKey || q.AdapterFieldKey]"
                class="mt-1"
              />
              <span>Другое (ввести вручную)</span>
            </label>

            <div v-if="isOtherSelected(q, idx)" class="ml-6 mt-1">
              <Textarea
                v-model="otherTexts[q.adapterFieldKey || q.AdapterFieldKey]"
                :placeholder="q.defaultRecommendation || 'Опишите атрибут или значение'"
                rows="2"
                class="text-sm"
              />
            </div>
          </div>

          <div v-if="q.impact" class="mt-1 text-[10px] text-muted-foreground">
            Влияние: {{ q.impact }}
          </div>
        </div>
      </div>

      <div v-else-if="!loading" class="py-4 text-sm text-muted-foreground">
        Вопросов нет. Можно продолжить генерацию маппинга.
      </div>

      <DialogFooter class="gap-2">
        <Button variant="outline" @click="handleCancel">Позже</Button>
        <Button
          @click="handleSubmit"
          :disabled="loading || submitting || !mappingId || !questions.length"
        >
          {{ submitting ? 'Отправка…' : 'Отправить и сгенерировать' }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<script setup>
import { ref, watch, computed } from 'vue';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/textarea';
import { displaySonnerSuccess, displaySonnerError } from '@/store/sonnerHelper';

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  mappingId: { type: [Number, String], default: null },
});

const emit = defineEmits(['update:modelValue', 'completed', 'cancel']);

const internalOpen = computed({
  get: () => props.modelValue,
  set: (val) => emit('update:modelValue', val),
});

const loading = ref(false);
const submitting = ref(false);
const questions = ref([]);
const answers = ref({});
const otherTexts = ref({});
const report = ref(null);
const currentMappingId = ref(null);

async function loadCachedQuestions(id) {
  if (!id) return;
  loading.value = true;
  questions.value = [];
  answers.value = {};
  report.value = null;
  currentMappingId.value = id;

  try {
    const { http } = await import('@/main');
    const res = await http.get(`/marketplace/mappings/${id}/compatibility-context`);
    const data = res.data || res;

    questions.value = data.questions || [];
    report.value = data.report || {};

    questions.value.forEach((q) => {
      const key = q.adapterFieldKey || q.AdapterFieldKey;
      if (key && q.defaultRecommendation && !answers.value[key]) {
        answers.value[key] = q.defaultRecommendation;
      }
    });
  } catch (e) {
    displaySonnerError(e);
    questions.value = [];
  } finally {
    loading.value = false;
  }
}

watch(() => props.mappingId, (newId) => {
  if (newId && props.modelValue) {
    loadCachedQuestions(newId);
  }
}, { immediate: true });

watch(() => props.modelValue, (open) => {
  if (open && props.mappingId) {
    loadCachedQuestions(props.mappingId);
  }
});

function getCandidateValue(cand) {
  if (cand.sourceAttributeId) return `attribute:${cand.sourceAttributeId}`;
  if (cand.sourceField) return `productField:${cand.sourceField}`;
  return cand.sourceAttributeName || cand.SourceAttributeName || '';
}

function isOtherSelected(q) {
  const key = q.adapterFieldKey || q.AdapterFieldKey;
  return answers.value[key] === '__other__';
}

async function handleSubmit() {
  if (!currentMappingId.value || !questions.value.length) return;

  const { http } = await import('@/main');
  const payloadAnswers = {};

  for (const [key, val] of Object.entries(answers.value)) {
    if (val === '__other__') {
      payloadAnswers[key] = otherTexts.value[key] || 'manual';
    } else {
      payloadAnswers[key] = val;
    }
  }

  submitting.value = true;
  try {
    const operationId = crypto.randomUUID().replace(/-/g, '');
    await http.post(`/ai/mapping-suggest/${currentMappingId.value}`, {
      operationId,
      answers: payloadAnswers,
    });

    internalOpen.value = false;

    displaySonnerSuccess('Ответы сохранены. AI генерирует маппинг в фоне…', {
      label: 'Открыть прогресс',
      onClick: () => {
        import('@/main').then((m) => {
          m.store?.dispatch?.('operations/openPanel');
        });
      },
    });

    emit('completed', { mappingId: currentMappingId.value, answers: payloadAnswers });
  } catch (e) {
    displaySonnerError(e);
  } finally {
    submitting.value = false;
  }
}

function handleCancel() {
  internalOpen.value = false;
  emit('cancel');
}
</script>