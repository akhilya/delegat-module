<script>
import Page from "@/components/page.vue";
import Button from "@/components/ui/button/Button.vue";
import Input from "@/components/ui/input/Input.vue";
import Logo from "@/components/logo.vue";
import {
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui/form";

import { toTypedSchema } from "@vee-validate/zod";
import { z } from "zod";
import { useForm } from "vee-validate";
import { useStore } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { useRouter } from "vue-router";
import { PAGE_SIGN_IN } from "@/router";
import { ref } from "vue";
import { Loader2 } from "lucide-vue-next";

export default {
  components: {
    Page,
    FormControl,
    FormDescription,
    FormField,
    FormItem,
    FormLabel,
    FormMessage,
    Button,
    Input,
    Logo,
    Loader2,
  },
  setup() {
    const store = useStore();
    const router = useRouter();
    const isSubmitting = ref(false);

    const formSchema = toTypedSchema(
      z
        .object({
          userName: z.string().min(4).max(45),
          password: z.string().min(8).max(128),
          confirmPassword: z.string().min(8).max(128),
          invitationToken: z.string(),
        })
        .refine((data) => data.password === data.confirmPassword, {
          message: "Пароли не совпадают",
          path: ["confirmPassword"],
        }),
    );

    const form = useForm({
      validationSchema: formSchema,
    });

    const onSubmit = form.handleSubmit(async (values) => {
      isSubmitting.value = true;
      await store.dispatch("postSignUpUser", {
        input: {
          userName: values.userName,
          password: values.password,
          confirmPassword: values.confirmPassword,
        },
        token: values.invitationToken,
        onSuccess: (response) => {
          displaySonnerSuccess("Успешная регистрация.");
          router.push(response.data.value.next);
        },
        onError: (error) => {
          if (error?.response?.data?.errors)
            form.setErrors(error.response.data.errors);
          displaySonnerError(error);
        },
      });
      isSubmitting.value = false;
    });

    return {
      form,
      onSubmit,
      isSubmitting,
      PAGE_SIGN_IN,
    };
  },
};
</script>

<template>
  <Page class="min-h-screen flex items-center justify-center bg-background">
    <div class="w-full max-w-md bg-card rounded-xl shadow-lg border p-8 sm:p-10">
      <div class="flex flex-col items-center mb-6">
        <Logo class="mb-4" />
        <h1 class="text-2xl font-semibold text-center">Регистрация</h1>
        <p class="text-sm text-muted-foreground text-center mt-1">
          Создайте аккаунт для панели управления
        </p>
      </div>

      <form @submit="onSubmit" class="space-y-4">
        <FormField v-slot="{ componentField }" name="userName">
          <FormItem>
            <FormLabel>Имя пользователя</FormLabel>
            <FormControl>
              <Input type="text" placeholder="Олег" v-bind="componentField" />
            </FormControl>
            <FormMessage />
          </FormItem>
        </FormField>

        <FormField v-slot="{ componentField }" name="password">
          <FormItem>
            <FormLabel>Пароль</FormLabel>
            <FormControl>
              <Input type="password" v-bind="componentField" />
            </FormControl>
            <FormMessage />
          </FormItem>
        </FormField>

        <FormField v-slot="{ componentField }" name="confirmPassword">
          <FormItem>
            <FormLabel>Подтвердите пароль</FormLabel>
            <FormControl>
              <Input type="password" v-bind="componentField" />
            </FormControl>
            <FormMessage />
          </FormItem>
        </FormField>

        <FormField v-slot="{ componentField }" name="invitationToken">
          <FormItem>
            <FormLabel>Токен приглашения</FormLabel>
            <FormControl>
              <Input
                type="text"
                placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
                v-bind="componentField"
              />
            </FormControl>
            <FormMessage />
          </FormItem>
        </FormField>

        <Button type="submit" size="lg" class="w-full mt-6" :disabled="isSubmitting">
          <p v-if="!isSubmitting">Создать аккаунт</p>
          <Loader2 v-else class="animate-spin" />
        </Button>
      </form>

      <p class="text-center text-sm text-muted-foreground mt-4">
        Есть аккаунт?
        <RouterLink :to="PAGE_SIGN_IN" class="text-foreground hover:underline">
          Войти.
        </RouterLink>
      </p>
    </div>
  </Page>
</template>
