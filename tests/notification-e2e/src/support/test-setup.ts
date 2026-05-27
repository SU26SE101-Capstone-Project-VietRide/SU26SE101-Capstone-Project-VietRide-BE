import axios from 'axios';

module.exports = async function () {
  // Notification NestJS service default port is 3002.
  const host = process.env.HOST ?? 'localhost';
  const port = process.env.PORT ?? '3002';
  axios.defaults.baseURL = `http://${host}:${port}`;
};
