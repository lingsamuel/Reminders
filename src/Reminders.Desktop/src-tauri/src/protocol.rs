use serde_json::{json, Value};

pub fn encode_request(id: u64, method: &str, payload: Value) -> Result<String, String> {
    if method.is_empty() || method.len() > 64 {
        return Err("无效的后端操作".into());
    }
    serde_json::to_string(&json!({ "id": id, "method": method, "payload": payload }))
        .map(|mut line| {
            line.push('\n');
            line
        })
        .map_err(|error| error.to_string())
}

pub fn decode_response(value: &Value) -> Result<Value, String> {
    if let Some(error) = value.get("error") {
        return Err(error.as_str().unwrap_or("后端操作失败").to_string());
    }
    value
        .get("result")
        .cloned()
        .ok_or_else(|| "后端响应缺少结果".into())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn request_is_one_line_even_when_title_contains_newlines() {
        let line = encode_request(7, "saveTask", json!({"title":"中文\n任务"})).unwrap();
        assert_eq!(line.lines().count(), 1);
        let value: Value = serde_json::from_str(&line).unwrap();
        assert_eq!(value["id"], 7);
        assert_eq!(value["payload"]["title"], "中文\n任务");
    }

    #[test]
    fn backend_failure_is_never_interpreted_as_success() {
        assert_eq!(
            decode_response(&json!({"id":1,"error":"磁盘已满"})),
            Err("磁盘已满".into())
        );
        assert!(decode_response(&json!({"id":1})).is_err());
        assert_eq!(
            decode_response(&json!({"id":1,"result":{"ready":true}})).unwrap()["ready"],
            true
        );
    }
}
