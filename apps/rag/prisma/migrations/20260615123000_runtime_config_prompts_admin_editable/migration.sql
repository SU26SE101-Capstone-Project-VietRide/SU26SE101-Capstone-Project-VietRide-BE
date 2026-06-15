SET search_path TO vietride_rag, public;

UPDATE runtime_configs
SET editable_group = 'admin'
WHERE key IN (
    'chat.system_prompt',
    'intent.classifier_prompt',
    'query_rewrite.prompt',
    'summary.prompt',
    'rerank.prompt'
);
