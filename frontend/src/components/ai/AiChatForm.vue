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
import { answersFromFormData, interpolateFollowUp } from "@/lib/ai/replyUi.js";
import { extractResponseData, displaySonnerError } from "@/store/sonnerHelper";

const props = defineProps({
  form: { type: Object, required: true },
});

const emit = defineEmits(["ask"]);
const store = useStore();
const { t } = useI18n();

const adapters = ref([]);
const loadingAdapters = ref(false);
const submitting = ref(false);
const done = ref(false);
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

function resetState() {
  done.value = false;
  error.value = "";
  for (const key of Object.keys(skipped)) delete skipped[key];
}

watch(
  () => props.form.id,
  () => {
    resetState();
  },
);

onMounted(() => {
  if (props.form.kind === "connection") loadAdapters();
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
  if (submitting.value || done.value) return;
  const formEl = event.target;
  if (!(formEl instanceof HTMLFormElement)) return;
  const values = answersFromFormData(
    questions.value,
    new FormData(formEl),
    skipped,
  );
  done.value = true;
  emit("ask", interpolateFollowUp(props.form.followUp, values));
}

function submitConnection() {
  if (submitting.value || done.value) return;
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
      emit(
        "ask",
        t("ai.form.connectionFollowUp", {
          name: data.name,
          adapter: data.adapterKey,
          id: created.id ?? "",
        }),
      );
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
  <div class="w-full min-w-0 rounded-xl bg-card p-3 ring-1 ring-inset ring-border">
    <p v-if="title" class="text-sm font-medium">{{ title }}</p>
    <p v-if="form.kind === 'connection'" class="mt-1 text-xs text-muted-foreground">
      {{ t("ai.form.secretsStayInBrowser") }}
    </p>

    <form
      v-if="form.kind === 'connection'"
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
        :disabled="submitting || done"
      />
      <p v-if="error" class="text-xs text-destructive">{{ error }}</p>
      <Button
        type="submit"
        size="sm"
        class="self-start"
        :disabled="submitting || done || loadingAdapters"
      >
        {{ done ? t("ai.form.connected") : submitLabel }}
      </Button>
    </form>

    <p v-else-if="done" class="mt-2 text-xs text-muted-foreground">
      {{ t("ai.form.sent") }}
    </p>

    <Questionnaire
      v-else
      class="mt-3"
      :default-item="questions[0]?.key"
      :items="questionnaireItems"
      @submit="submitQuestions"
    >
      <QuestionnaireProgress v-if="questions.length > 1" class="text-muted-foreground">
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
        <QuestionnaireSubmit size="sm">
          {{ submitLabel }}
        </QuestionnaireSubmit>
      </QuestionnaireActions>
    </Questionnaire>
  </div>
</template>
