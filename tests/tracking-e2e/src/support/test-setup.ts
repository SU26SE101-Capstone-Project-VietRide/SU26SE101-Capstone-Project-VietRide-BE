import axios from 'axios';

module.exports = async function () {
  // Tracking NestJS service default port is 3001.
  const host = process.env.HOST ?? 'localhost';
  const port = process.env.PORT ?? '3001';
  axios.defaults.baseURL = `http://${host}:${port}`;
};
