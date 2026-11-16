ARG NODE_VERSION=24

FROM node:${NODE_VERSION}-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npx ng build --configuration production

FROM nginx:1.27-alpine AS final
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist/front/browser /usr/share/nginx/html
EXPOSE 80