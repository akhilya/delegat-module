# Build stage
FROM node:22-alpine AS build

WORKDIR /app

# Copy package files
COPY package*.json ./

# Install dependencies
RUN npm ci

# Copy source code
COPY . .

# Build arguments
ARG VITE_API_SERVER=/api
ENV VITE_API_SERVER=$VITE_API_SERVER
# Empty default: CI/release must pass SemVer from root VERSION. Treat unset as unknown in the SPA (do not bake "dev").
ARG VITE_APP_VERSION=
ENV VITE_APP_VERSION=$VITE_APP_VERSION
ARG VITE_DOCS_PUBLIC_URL=https://docs.module.delegat.pro
ENV VITE_DOCS_PUBLIC_URL=$VITE_DOCS_PUBLIC_URL

# Build the application
RUN npm run build

# Production stage
FROM nginx:alpine

# Copy built files from build stage
COPY --from=build /app/dist /usr/share/nginx/html

# Runtime-rendered nginx config — API_UPSTREAM must be unique on shared Docker networks.
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
ENV API_UPSTREAM=delegat-api
# Overwritten per instance from CP BuildEnvFile (see docker-entrypoint.d).
ENV DOCS_PUBLIC_URL=https://docs.module.delegat.pro
COPY docker-entrypoint.d/40-runtime-config.sh /docker-entrypoint.d/40-runtime-config.sh
RUN chmod +x /docker-entrypoint.d/40-runtime-config.sh

# Expose port 80
EXPOSE 80

# nginx image entrypoint runs envsubst on /etc/nginx/templates/*.template
# and scripts under /docker-entrypoint.d/
CMD ["nginx", "-g", "daemon off;"]
