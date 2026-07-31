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
ARG VITE_APP_VERSION=dev
ENV VITE_APP_VERSION=$VITE_APP_VERSION

# Build the application
RUN npm run build

# Production stage
FROM nginx:alpine

# Copy built files from build stage
COPY --from=build /app/dist /usr/share/nginx/html

# Runtime-rendered nginx config — API_UPSTREAM must be unique on shared Docker networks.
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
ENV API_UPSTREAM=delegat-api

# Expose port 80
EXPOSE 80

# nginx image entrypoint runs envsubst on /etc/nginx/templates/*.template
CMD ["nginx", "-g", "daemon off;"]
