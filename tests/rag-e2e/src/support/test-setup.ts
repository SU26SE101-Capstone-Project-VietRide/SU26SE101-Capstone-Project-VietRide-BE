import axios from 'axios';

module.exports = async function () {
  // RAG NestJS service default port is 3003.
  const host = process.env.HOST ?? 'localhost';
  const port = process.env.PORT ?? '3003';
  axios.defaults.baseURL = `http://${host}:${port}`;
};
