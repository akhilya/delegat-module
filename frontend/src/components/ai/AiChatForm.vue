<script setup>
import { computed, onMounted, reactive, ref, watch } from "vue";
import { useStore } from "vuex";
import { useI18n } from "vue-i18n";
import { Button } from "@/components/ui/button";
import ConnectionCreateForm from "@/components/marketplace/ConnectionCreateForm.vue";
import {
  Questionnaire,
  QuestionnaireActions,
  QuestionnaireChoice,
  QuestionnaireChoiceDescription,
  QuestionnaireChoices,
  QuestionnaireDescription,
  QuestionnaireError,
  QuestionnaireInput,
  QuestionnaireItem,
  QuestionnaireNext,
  QuestionnairePrevious,
  QuestionnaireProgress,
  QuestionnaireSkip,
  QuestionnaireSubmit,
  QuestionnaireTitle,
} from "@/components/ui/questionnaire";
import {
  answersFromFormData,
  interpolateFollowUp,
  recapFormAnswers,
} from "@/lib/ai/replyUi.js";
import { extractResponseData, displaySonnerError } from "@/store/sonnerHelper";

const props = defineProps({
  form: { type: Object, required: true },
  submitted: { type: Boolean, default: false },
  answers: { type: Object, default: null },
  disabled: { type: Boolean, default: false },
  inactive: { type: Boolean, default: false },
});

const emit = defineEmits(["submit"]);
const store = useStore();
const { t } = useI18n();

const adapters = ref([]);
const loadingAdapters = ref(false);
const submitting = ref(false);
const done = ref(false);
const localAnswers = ref(null);
const error = ref("");
const createForm = ref(null);
const skipped = reactive({});

const title = computed(() => {
  if (props.form.title) return props.form.title;
  if (props.form.kind === "connection") return t("ai.form.connectTitle");
  return "";
});
const submitLabel = computed(
  () => props.form.submitLabel || t("ai.form.submit"),
);
const questions = computed(() => props.form.questions || []);
const questionnaireItems = computed(() =>
  questions.value.map((question) => ({
    name: question.key,
    required: question.required,
    choices: question.choices?.map((choice) => ({ value: choice.value })),
  })),
);
const isSubmitted = computed(() => props.submitted || done.value);
const recap = computed(() =>
  recapFormAnswers(questions.value, props.answers || localAnswers.value || {}),
);

function resetState() {
  done.value = props.submitted;
  if (!props.submitted) localAnswers.value = null;
  error.value = "";
  for (const key of Object.keys(skipped)) delete skipped[key];
}

watch(
  () => props.form.id,
  () => {
    resetState();
  },
);

watch(
  () => props.submitted,
  (value) => {
    if (value) done.value = true;
  },
  { immediate: true },
);

onMounted(() => {
  if (props.form.kind === "connection" && !props.inactive && !isSubmitted.value)
    loadAdapters();
});

function loadAdapters() {
  loadingAdapters.value = true;
  store.dispatch("getAvailableAdapters", {
    onSuccess: (response) => {
      adapters.value = extractResponseData(response, []) || [];
      loadingAdapters.value = false;
    },
    onError: (err) => {
      loadingAdapters.value = false;
      error.value = t("ai.form.adaptersFailed");
      displaySonnerError(err);
    },
  });
}

function onQuestionStatus(key, status) {
  skipped[key] = status;
}

function submitQuestions(event) {
  event.preventDefault();
  if (submitting.value || isSubmitted.value || props.disabled || props.inactive)
    return;
  const formEl = event.target;
  if (!(formEl instanceof HTMLFormElement)) return;
  const values = answersFromFormData(
    questions.value,
    new FormData(formEl),
    skipped,
  );
  // The parent accepts or rejects the reply; only its `submitted` prop turns
  // this form into a read-only recap.
  localAnswers.value = values;
  emit("submit", {
    formId: props.form.id,
    answers: values,
    prompt: interpolateFollowUp(props.form.followUp, values),
  });
}

function submitConnection() {
  if (submitting.value || isSubmitted.value || props.disabled || props.inactive)
    return;
  const data = createForm.value?.payload?.();
  if (!data?.adapterKey || !createForm.value?.canSubmit) {
    error.value = t("marketplace.marketplacePage.enterCredentials");
    return;
  }
  submitting.value = true;
  error.value = "";
  store.dispatch("createConnection", {
    data,
    onSuccess: (response) => {
      submitting.value = false;
      const created = extractResponseData(response, {});
      done.value = true;
      emit("submit", {
        formId: props.form.id,
        answers: {
          name: data.name,
          adapter: data.adapterKey,
          id: created.id ?? "",
        },
        prompt: t("ai.form.connectionFollowUp", {
          name: data.name,
          adapter: data.adapterKey,
          id: created.id ?? "",
        }),
      });
    },
    onError: (err) => {
      submitting.value = false;
      displaySonnerError(err);
      error.value = t("ai.form.connectFailed");
    },
  });
}
</script>

<template>
  <div
    class="w-full min-w-0 rounded-xl bg-card p-3 ring-1 ring-inset ring-border"
  >
    <p v-if="title" class="text-sm font-medium">{{ title }}</p>
    <p
      v-if="form.kind === 'connection'"
      class="mt-1 text-xs text-muted-foreground"
    >
      {{ t("ai.form.secretsStayInBrowser") }}
    </p>

    <p
      v-if="!isSubmitted && inactive"
      class="mt-3 text-xs text-muted-foreground"
    >
      {{ t("ai.form.inactive") }}
    </p>

    <form
      v-else-if="form.kind === 'connection'"
      class="mt-3 flex flex-col gap-3"
      @submit.prevent="submitConnection"
    >
      <p v-if="loadingAdapters" class="text-xs text-muted-foreground">
        {{ t("ai.form.loadingAdapters") }}
      </p>
      <ConnectionCreateForm
        v-else
        ref="createForm"
        quiet
        :show-active-switch="false"
        id-prefix="ai-conn"
        :adapters="adapters"
        :initial-adapter-key="form.adapterKey"
        :initial-name="form.name"
        :lock-adapter="!!form.adapterKey"
        :disabled="submitting || isSubmitted || disabled"
      />
      <p v-if="error" class="text-xs text-destructive">{{ error }}</p>
      <Button
        type="submit"
        size="sm"
        class="self-start"
        :disabled="submitting || isSubmitted || loadingAdapters || disabled"
      >
        {{ isSubmitted ? t("ai.form.connected") : submitLabel }}
      </Button>
    </form>

    <div v-else-if="isSubmitted" class="mt-3 flex flex-col gap-3">
      <div v-for="row in recap" :key="row.key" class="min-w-0">
        <p class="text-xs text-muted-foreground">{{ row.title }}</p>
        <p class="text-sm leading-snug">{{ row.answer }}</p>
      </div>
      <p v-if="!recap.length" class="text-xs text-muted-foreground">
        {{ t("ai.form.answered") }}
      </p>
    </div>

    <Questionnaire
      v-else
      class="mt-3"
      :default-item="questions[0]?.key"
      :items="questionnaireItems"
      @submit="submitQuestions"
    >
      <QuestionnaireProgress
        v-if="questions.length > 1"
        class="text-muted-foreground"
      >
        <template #default="{ current, total }">
          {{ t("ai.form.progress", { current, total }) }}
        </template>
      </QuestionnaireProgress>

      <QuestionnaireItem
        v-for="question in questions"
        :key="question.key"
        :name="question.key"
        :required="question.required"
        :multiple="question.type === 'multiple'"
        @update:status="onQuestionStatus(question.key, $event)"
      >
        <QuestionnaireTitle>{{ question.title }}</QuestionnaireTitle>
        <QuestionnaireDescription v-if="question.description">
          {{ question.description }}
        </QuestionnaireDescription>
        <QuestionnaireChoices>
          <QuestionnaireChoice
            v-for="choice in question.choices"
            :key="choice.value"
            :value="choice.value"
          >
            <span class="font-medium">{{ choice.label }}</span>
            <QuestionnaireChoiceDescription v-if="choice.description">
              {{ choice.description }}
            </QuestionnaireChoiceDescription>
          </QuestionnaireChoice>
          <QuestionnaireInput
            v-if="question.type === 'text' || question.allowOther"
            :aria-label="question.title"
            :placeholder="question.placeholder || t('ai.form.otherPlaceholder')"
          />
        </QuestionnaireChoices>
        <QuestionnaireError>
          {{
            question.required
              ? t("ai.form.requiredAnswer")
              : t("ai.form.optionalAnswer")
          }}
        </QuestionnaireError>
      </QuestionnaireItem>

      <QuestionnaireActions>
        <QuestionnairePrevious size="sm">
          {{ t("ai.form.previous") }}
        </QuestionnairePrevious>
        <QuestionnaireSkip size="sm">
          {{ t("ai.form.skip") }}
        </QuestionnaireSkip>
        <QuestionnaireNext size="sm">
          {{ t("ai.form.next") }}
        </QuestionnaireNext>
        <QuestionnaireSubmit size="sm" :disabled="disabled">
          {{ submitLabel }}
        </QuestionnaireSubmit>
      </QuestionnaireActions>
    </Questionnaire>
  </div>
</template>
